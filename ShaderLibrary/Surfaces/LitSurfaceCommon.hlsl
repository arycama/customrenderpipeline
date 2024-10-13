﻿#ifndef LIT_SURFACE_COMMON_INCLUDED
#define LIT_SURFACE_COMMON_INCLUDED

#include "../Common.hlsl"
#include "../Samplers.hlsl"
#include "../Exposure.hlsl"
#include "../Utility.hlsl"

cbuffer UnityPerMaterial
{
	float4 _DetailAlbedoMap_ST, _MainTex_ST;
	float4 _Color;
	float3 _EmissionColor;
	float _BumpScale, _Cutoff, _DetailNormalMapScale, _Metallic, _Smoothness;
	float _HeightBlend, _NormalBlend;
	float BentNormal, _EmissiveExposureWeight;
	float _Anisotropy;
	float Smoothness_Source;
	float _Parallax;
	float Terrain_Blending;
	float Blurry_Refractions;
	float Anisotropy;
	float _TriplanarSharpness;
};

Texture2D<float4> _BentNormal, _MainTex, _BumpMap, _MetallicGlossMap, _DetailAlbedoMap, _DetailNormalMap, _OcclusionMap, _ParallaxMap;
Texture2D<float3> _EmissionMap;
Texture2D<float> _AnisotropyMap;

struct SurfaceInput
{
	float2 uv;
	float3 worldPosition;
	float3 vertexNormal;
	float3 vertexTangent;
	float tangentSign; // Todo: bool?
	bool isFrontFace;
};

struct SurfaceOutput
{
	float3 albedo;
	float alpha;
	float metallic;
	float3 normal;
	float roughness;
	float occlusion;
	float3 bentNormal;
	float3 emission;
};

float4 SampleTexture(Texture2D<float4> tex, float2 uv, bool isRaytracing)
{
	// TODO: Ray cones
	if(isRaytracing)
		return tex.SampleLevel(_LinearRepeatSampler, uv, 0.0);
	else
		return tex.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias);
}

float3 SampleTexture(Texture2D<float3> tex, float2 uv, bool isRaytracing)
{
	// TODO: Ray cones
	if(isRaytracing)
		return tex.SampleLevel(_LinearClampSampler, uv, 0.0);
	else
		return tex.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias);
}

// TODO: World position only required for triplanar, is there a better way?
SurfaceOutput GetSurfaceAttributes(SurfaceInput input, bool isRaytracing = false)
{
	SurfaceOutput result;
	
	float3 vertexNormal = input.isFrontFace ? input.vertexNormal : -input.vertexNormal;
	float2 uv = ApplyScaleOffset(input.uv, _MainTex_ST);
	float2 detailUv = ApplyScaleOffset(input.uv, _DetailAlbedoMap_ST);
	
	#ifdef TRIPLANAR_ON
		float3 absoluteWorldPosition = input.worldPosition + _ViewPosition;
		float3 flip = vertexNormal < 0.0 ? 1.0 : -1.0;
		float2 triplanarUvX = ApplyScaleOffset(absoluteWorldPosition.zy * float2(-flip.x, 1.0), _MainTex_ST);
		float2 triplanarUvY = ApplyScaleOffset(absoluteWorldPosition.xz * float2(-flip.y, 1.0),  _MainTex_ST);
		float2 triplanarUvZ = ApplyScaleOffset(absoluteWorldPosition.xy * float2(flip.z, 1.0), _MainTex_ST);
		float3 triplanarWeights = pow(abs(vertexNormal), _TriplanarSharpness);
		triplanarWeights *= rcp(triplanarWeights.x + triplanarWeights.y + triplanarWeights.z);
	
		float4 albedoAlpha = SampleTexture(_MainTex, triplanarUvX, isRaytracing) * triplanarWeights.x;
		albedoAlpha += SampleTexture(_MainTex, triplanarUvY, isRaytracing) * triplanarWeights.y;
		albedoAlpha += SampleTexture(_MainTex, triplanarUvZ, isRaytracing) * triplanarWeights.z;
		
		float3 tnormalX = UnpackNormalAG(SampleTexture(_BumpMap, triplanarUvX, isRaytracing), _BumpScale);
		float3 tnormalY = UnpackNormalAG(SampleTexture(_BumpMap, triplanarUvY, isRaytracing), _BumpScale);
		float3 tnormalZ = UnpackNormalAG(SampleTexture(_BumpMap, triplanarUvZ, isRaytracing), _BumpScale);
		
		// minor optimization of sign(). prevents return value of 0
		float3 axisSign = vertexNormal < 0 ? -1 : 1;
		float3 absVertNormal = abs(vertexNormal);

		// swizzle world normals to match tangent space and apply reoriented normal mapping blend
		tnormalX = FromToRotationZ(float3(vertexNormal.zy, absVertNormal.x), tnormalX);
		tnormalY = FromToRotationZ(float3(vertexNormal.xz, absVertNormal.y), tnormalY);
		tnormalZ = FromToRotationZ(float3(vertexNormal.xy, absVertNormal.z), tnormalZ);

		// apply world space sign to tangent space Z
		tnormalX.z *= axisSign.x;
		tnormalY.z *= axisSign.y;
		tnormalZ.z *= axisSign.z;

		// sizzle tangent normals to match world normal and blend together
		result.normal = result.bentNormal = normalize(tnormalX.zyx * triplanarWeights.x + tnormalY.xzy * triplanarWeights.y + tnormalZ.xyz * triplanarWeights.z);
	#else
		float4 albedoAlpha = SampleTexture(_MainTex, uv, isRaytracing);
		float4 detail = SampleTexture(_DetailAlbedoMap, detailUv, isRaytracing);
		albedoAlpha.rgb = albedoAlpha.rgb * detail.rgb * 2;
		
		float3 normalTS = UnpackNormalAG(SampleTexture(_BumpMap, uv, isRaytracing), _BumpScale);
	
		// Detail Normal Map
		float3 detailNormalTangent = UnpackNormalAG(SampleTexture(_DetailNormalMap, detailUv, isRaytracing), _DetailNormalMapScale);
		normalTS = BlendNormalRNM(normalTS, detailNormalTangent);
		
		float3x3 tangentToWorld = TangentToWorldMatrix(vertexNormal, input.vertexTangent, input.tangentSign);
		result.normal = normalize(mul(normalTS, tangentToWorld));
		result.bentNormal = result.normal;
		
		if(BentNormal)
		{
			float3 bentNormalTS = UnpackNormalAG(SampleTexture(_BentNormal, uv, isRaytracing));
			result.bentNormal = normalize(mul(bentNormalTS, tangentToWorld));
		}
	#endif

	result.albedo = albedoAlpha.rgb * _Color.rgb;
	result.alpha = albedoAlpha.a * _Color.a;

	// TODO: Triplanar?
	float4 metallicGloss = SampleTexture(_MetallicGlossMap, uv, isRaytracing);
	result.metallic = metallicGloss.r * _Metallic;

	float smoothness;
	if (Smoothness_Source)
		smoothness = albedoAlpha.a * _Smoothness;
	else
		smoothness = metallicGloss.a * _Smoothness;

	result.roughness = SmoothnessToPerceptualRoughness(smoothness);

	float3 emission = SampleTexture(_EmissionMap, uv, isRaytracing) * _EmissionColor;
	result.emission = ApplyEmissiveExposureWeight(emission, _EmissiveExposureWeight);

	result.occlusion = SampleTexture(_OcclusionMap, uv, isRaytracing).g;
	return result;
}

#endif