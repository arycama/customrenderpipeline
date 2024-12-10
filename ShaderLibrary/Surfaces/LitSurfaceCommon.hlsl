#ifndef LIT_SURFACE_COMMON_INCLUDED
#define LIT_SURFACE_COMMON_INCLUDED

#include "../Common.hlsl"
#include "../Samplers.hlsl"
#include "../Exposure.hlsl"
#include "../Raytracing.hlsl"
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

float4 _BentNormal_TexelSize,_MainTex_TexelSize, _BumpMap_TexelSize, _MetallicGlossMap_TexelSize, _DetailAlbedoMap_TexelSize, _DetailNormalMap_TexelSize, _OcclusionMap_TexelSize, _ParalllaxMap_TexelSize, _EmissionMap_TexelSize,
_AnisotropyMap_TexelSize;

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

float4 SampleTexture(Texture2D<float4> tex, float2 uv, float2 texelSize, bool isRaytracing = false, float3 worldNormal = 0, float coneWidth = 0, float scale = 1)
{
	// TODO: Ray cones
	if(isRaytracing)
	{
		float lod = 0 * ComputeTextureLOD(texelSize, worldNormal, coneWidth, scale);
		return tex.SampleLevel(_TrilinearRepeatSampler, uv, lod);
	}
	else
		return tex.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias);
	}

float3 SampleTexture(Texture2D<float3> tex, float2 uv, float2 texelSize, bool isRaytracing = false, float3 worldNormal = 0, float coneWidth = 0, float scale = 1)
{
	// TODO: Ray cones
	if(isRaytracing)
	{
		float lod = 0 * ComputeTextureLOD(texelSize, worldNormal, coneWidth, scale);
		return tex.SampleLevel(_TrilinearRepeatSampler, uv, lod);
	}
	else
		return tex.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias);
}

// TODO: World position only required for triplanar, is there a better way?
SurfaceOutput GetSurfaceAttributes(SurfaceInput input, bool isRaytracing = false, float3 worldNormal = 0, float coneWidth = 0)
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
	
		float4 albedoAlpha = SampleTexture(_MainTex, triplanarUvX, _MainTex_TexelSize.zw, isRaytracing, worldNormal, coneWidth) * triplanarWeights.x;
		albedoAlpha += SampleTexture(_MainTex, triplanarUvY, _MainTex_TexelSize.zw, isRaytracing, worldNormal, coneWidth) * triplanarWeights.y;
		albedoAlpha += SampleTexture(_MainTex, triplanarUvZ, _MainTex_TexelSize.zw, isRaytracing, worldNormal, coneWidth) * triplanarWeights.z;
		
		float3 tnormalX = UnpackNormal(SampleTexture(_BumpMap, triplanarUvX, _BumpMap_TexelSize.zw, isRaytracing, worldNormal, coneWidth), _BumpScale);
		float3 tnormalY = UnpackNormal(SampleTexture(_BumpMap, triplanarUvY, _BumpMap_TexelSize.zw, isRaytracing, worldNormal, coneWidth), _BumpScale);
		float3 tnormalZ = UnpackNormal(SampleTexture(_BumpMap, triplanarUvZ, _BumpMap_TexelSize.zw, isRaytracing, worldNormal, coneWidth), _BumpScale);
		
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
		float4 albedoAlpha = SampleTexture(_MainTex, uv, _MainTex_TexelSize.zw, isRaytracing, worldNormal, coneWidth);
		float4 detail = SampleTexture(_DetailAlbedoMap, detailUv, _DetailAlbedoMap_TexelSize.zw, isRaytracing, worldNormal, coneWidth);
		albedoAlpha.rgb = albedoAlpha.rgb * detail.rgb * 2;
		
		float3 normalTS = UnpackNormal(SampleTexture(_BumpMap, uv, _BumpMap_TexelSize.zw, isRaytracing, worldNormal, coneWidth), _BumpScale);
	
		// Detail Normal Map
		float3 detailNormalTangent = UnpackNormal(SampleTexture(_DetailNormalMap, detailUv, _DetailNormalMap_TexelSize.zw, isRaytracing, worldNormal, coneWidth), _DetailNormalMapScale);
		normalTS = BlendNormalRNM(normalTS, detailNormalTangent);
		
		float3x3 tangentToWorld = TangentToWorldMatrix(vertexNormal, input.vertexTangent, input.tangentSign);
		result.normal = normalize(mul(normalTS, tangentToWorld));
		result.bentNormal = result.normal;
		
		if(BentNormal)
		{
			float3 bentNormalTS = UnpackNormal(SampleTexture(_BentNormal, uv, _BentNormal_TexelSize.zw, isRaytracing, worldNormal, coneWidth));
			result.bentNormal = normalize(mul(bentNormalTS, tangentToWorld));
		}
	#endif

	result.albedo = albedoAlpha.rgb * _Color.rgb;
	result.alpha = albedoAlpha.a * _Color.a;

	// TODO: Triplanar?
	float4 metallicGloss = SampleTexture(_MetallicGlossMap, uv, _MetallicGlossMap_TexelSize.zw, isRaytracing, worldNormal, coneWidth);
	result.metallic = metallicGloss.r * _Metallic;

	float smoothness;
	if (Smoothness_Source)
		smoothness = albedoAlpha.a * _Smoothness;
	else
		smoothness = metallicGloss.a * _Smoothness;

	result.roughness = SmoothnessToPerceptualRoughness(smoothness);

	float3 emission = SampleTexture(_EmissionMap, uv, _EmissionMap_TexelSize.zw, isRaytracing, worldNormal, coneWidth) * _EmissionColor;
	result.emission = ApplyEmissiveExposureWeight(emission, _EmissiveExposureWeight);

	result.occlusion = SampleTexture(_OcclusionMap, uv, _OcclusionMap_TexelSize.zw, isRaytracing, worldNormal, coneWidth).r;
	return result;
}

#endif