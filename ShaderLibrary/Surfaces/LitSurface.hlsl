#include "../Common.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Material.hlsl"
#include "../Samplers.hlsl"
#include "../VolumetricLight.hlsl"
#include "../Temporal.hlsl"
#include "../Random.hlsl"

#ifdef __INTELLISENSE__
	#define UNITY_PASS_DEFERRED
	#define MOTION_VECTORS_ON
	#define RAYTRACING_ON
#endif

struct VertexInput
{
	uint instanceID : SV_InstanceID;
	float3 position : POSITION;
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE) || defined(MODE_TRANSPARENT)
		float2 uv : TEXCOORD;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float3 previousPosition : TEXCOORD4;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		float3 normal : NORMAL;
		float4 tangent : TANGENT;
		float3 color : COLOR;
	#endif
};

struct FragmentInput
{
	float4 position : SV_Position;
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE) || defined(MODE_TRANSPARENT)
		float2 uv : TEXCOORD;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		float3 worldPosition : POSITION1;
		float3 normal : NORMAL;
		float4 tangent : TANGENT;
		float3 color : COLOR;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float4 previousPositionCS : POSITION2;
	#endif
};

struct FragmentOutput
{
	#if defined (UNITY_PASS_DEFERRED) || defined(MOTION_VECTORS_ON)
		GBufferOutput gbuffer;
	
		#ifdef MOTION_VECTORS_ON
			float2 velocity : SV_Target4;
		#endif
	#else
		float4 color : SV_Target0;
	#endif
};

Texture2D<float4> _BentNormal, _MainTex, _BumpMap, _MetallicGlossMap, _DetailAlbedoMap, _DetailNormalMap, _EmissionMap, _OcclusionMap, _ParallaxMap;
Texture2D<float> _AnisotropyMap;

//cbuffer UnityPerMaterial
//{
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
//};

FragmentInput Vertex(VertexInput input)
{
	float3 worldPosition = ObjectToWorld(input.position, input.instanceID);
	worldPosition = PlanetCurve(worldPosition);
	
	FragmentInput output;
	output.position = WorldToClip(worldPosition);
	
	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(_ALHPATEST_ON) || defined(MODE_TRANSPARENT)
		output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
	#endif
	
	#ifndef UNITY_PASS_SHADOWCASTER
		output.worldPosition = worldPosition;
		output.normal = ObjectToWorldNormal(input.normal, input.instanceID, true);
		output.tangent = float4(ObjectToWorldDirection(input.tangent.xyz, input.instanceID, true), input.tangent.w * unity_WorldTransformParams.w);
		output.color = input.color;
	#endif
	
	#ifdef MOTION_VECTORS_ON
		float3 previousWorldPosition = PreviousObjectToWorld(unity_MotionVectorsParams.x ? input.previousPosition : input.position, input.instanceID);
		previousWorldPosition.y += sqrt(Sq(_PlanetRadius) - SqrLength(previousWorldPosition.xz)) - _PlanetRadius;
		output.previousPositionCS = WorldToClipPrevious(previousWorldPosition);
	#endif
	
	return output;
}

// Reoriented Normal Mapping
// http://blog.selfshadow.com/publications/blending-in-detail/
// Altered to take normals (-1 to 1 ranges) rather than unsigned normal maps (0 to 1 ranges)
float3 blend_rnm(float3 n1, float3 n2)
{
    n1.z += 1;
    n2.xy = -n2.xy;

    return n1 * dot(n1, n2) / n1.z - n2;
}

FragmentOutput Fragment(FragmentInput input, bool isFrontFace : SV_IsFrontFace)
{
	#ifdef TRIPLANAR_ON
		float3 absoluteWorldPosition = input.worldPosition + _ViewPosition;
		float3 flip = input.normal < 0.0 ? 1.0 : -1.0;
		float2 triplanarUvX = ApplyScaleOffset(absoluteWorldPosition.zy * float2(-flip.x, 1.0), _MainTex_ST);
		float2 triplanarUvY = ApplyScaleOffset(absoluteWorldPosition.xz * float2(-flip.y, 1.0),  _MainTex_ST);
		float2 triplanarUvZ = ApplyScaleOffset(absoluteWorldPosition.xy * float2(flip.z, 1.0), _MainTex_ST);
		float3 triplanarWeights = pow(abs(input.normal), _TriplanarSharpness);
		triplanarWeights *= rcp(triplanarWeights.x + triplanarWeights.y + triplanarWeights.z);
	#endif

	#if !defined(UNITY_PASS_SHADOWCASTER) || defined(MODE_CUTOUT) || defined(MODE_FADE)|| defined(MODE_TRANSPARENT)
		float2 uv = ApplyScaleOffset(input.uv, _MainTex_ST);
		float2 detailUv = ApplyScaleOffset(input.uv, _DetailAlbedoMap_ST);

		#ifdef TRIPLANAR_ON
			float4 albedoAlpha = _MainTex.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvX, _MipBias) * triplanarWeights.x;
			albedoAlpha += _MainTex.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvY, _MipBias) * triplanarWeights.y;
			albedoAlpha += _MainTex.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvZ, _MipBias) * triplanarWeights.z;
		#else
			float4 albedoAlpha = _MainTex.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias);
		#endif
	
		float4 detail = _DetailAlbedoMap.SampleBias(_TrilinearRepeatAniso16Sampler, detailUv, _MipBias);
		float3 albedo = albedoAlpha.rgb * detail.rgb * 2;

		albedo = albedo.rgb * _Color.rgb;
		float alpha = albedoAlpha.a * _Color.a;

		#ifdef MODE_CUTOUT
			clip(alpha - _Cutoff);
		#endif
	#endif
		
	FragmentOutput output;
	
	#if defined(UNITY_PASS_SHADOWCASTER) 
		#if defined(MODE_FADE) || defined(MODE_TRANSPARENT)
			clip(alpha - Noise1D(input.position.xy));
		#endif
	#else
		input.normal = isFrontFace ? input.normal : -input.normal;
	
		#ifdef TRIPLANAR_ON
			float3 tnormalX = UnpackNormalAG(_BumpMap.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvX, _MipBias), _BumpScale);
			float3 tnormalY = UnpackNormalAG(_BumpMap.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvY, _MipBias), _BumpScale);
			float3 tnormalZ = UnpackNormalAG(_BumpMap.SampleBias(_TrilinearRepeatAniso16Sampler, triplanarUvZ, _MipBias), _BumpScale);
		#else
			float3 normalTS = UnpackNormalAG(_BumpMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias), _BumpScale);
	
			// Detail Normal Map
			float3 detailNormalTangent = UnpackNormalAG(_DetailNormalMap.SampleBias(_TrilinearRepeatAniso16Sampler, detailUv, _MipBias), _DetailNormalMapScale);
			normalTS = BlendNormalRNM(normalTS, detailNormalTangent);
		#endif

		float4 metallicGloss = _MetallicGlossMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias);
		float metallic = metallicGloss.r * _Metallic;

		float anisotropy = Anisotropy ? _AnisotropyMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias).r : _Anisotropy;
		anisotropy = 2 * anisotropy - 1;
		float3 tangentWS = input.tangent;

		float perceptualSmoothness;
		if (Smoothness_Source)
			perceptualSmoothness = albedoAlpha.a * _Smoothness;
		else
			perceptualSmoothness = metallicGloss.a * _Smoothness;

		//perceptualSmoothness = GeometricNormalFiltering(perceptualSmoothness, input.normal,  0.25, 0.25);
		perceptualSmoothness = ProjectedSpaceGeometricNormalFiltering(perceptualSmoothness, input.normal, _SpecularAAScreenSpaceVariance, _SpecularAAThreshold);
	
		float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(perceptualSmoothness);

		// convert pRoughness/aniso to pRoughnessT/B
		//float PerceptualRoughness = roughness * sqrt(1 + anisotropy * float2(1, -1));

		float3 emission = _EmissionMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias).rgb * _EmissionColor;
		emission = lerp(emission * _Exposure, emission, _EmissiveExposureWeight);

		// Occlusion, no keyword?
		float occlusion = _OcclusionMap.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias).g;
		float3 diffuse = lerp(albedo, 0.0, metallic);
	
	#ifdef TRIPLANAR_ON
		// minor optimization of sign(). prevents return value of 0
		float3 axisSign = input.normal < 0 ? -1 : 1;
	
		float3 absVertNormal = abs(input.normal);

		// swizzle world normals to match tangent space and apply reoriented normal mapping blend
		tnormalX = blend_rnm(float3(input.normal.zy, absVertNormal.x), tnormalX);
		tnormalY = blend_rnm(float3(input.normal.xz, absVertNormal.y), tnormalY);
		tnormalZ = blend_rnm(float3(input.normal.xy, absVertNormal.z), tnormalZ);

		// apply world space sign to tangent space Z
		tnormalX.z *= axisSign.x;
		tnormalY.z *= axisSign.y;
		tnormalZ.z *= axisSign.z;

		// sizzle tangent normals to match world normal and blend together
		float3 normal = normalize(tnormalX.zyx * triplanarWeights.x + tnormalY.xzy * triplanarWeights.y + tnormalZ.xyz * triplanarWeights.z);
		float3 bentNormal = normal;
	#else
		float3x3 tangentToWorld = TangentToWorldMatrix(input.normal, input.tangent.xyz, input.tangent.w);
		float3 normal = normalize(mul(normalTS, tangentToWorld));
	
		float3 bentNormal = normal;
		if(BentNormal)
		{
			float3 bentNormalTS = UnpackNormalAG(_BentNormal.SampleBias(_TrilinearRepeatAniso16Sampler, uv, _MipBias));
			bentNormal = normalize(mul(bentNormalTS, tangentToWorld));
		}
	#endif
		#if defined(UNITY_PASS_DEFERRED) || defined(MOTION_VECTORS_ON)
			output.gbuffer = OutputGBuffer(albedo, metallic, normal, perceptualRoughness, bentNormal, occlusion, emission);
		
			#ifdef MOTION_VECTORS_ON
				output.velocity = CalculateVelocity(input.position.xy, input.previousPositionCS);
			#endif
		#else
			#ifdef MODE_TRANSPARENT
				albedo *= alpha;
			#endif
		
			LightingInput lightingInput;
			lightingInput.normal = normal;
			lightingInput.worldPosition = input.worldPosition;
			lightingInput.pixelPosition = input.position.xy;
			lightingInput.eyeDepth = input.position.w;
			lightingInput.albedo = albedo;
			lightingInput.f0 = lerp(0.04, albedo, metallic);
			lightingInput.perceptualRoughness = perceptualRoughness;
			lightingInput.occlusion = occlusion;
			lightingInput.translucency = 0.0;
			lightingInput.bentNormal = bentNormal;
			lightingInput.isWater = false;
			lightingInput.uv = input.position.xy * _ScaledResolution.zw;
		
			float3 lighting = GetLighting(lightingInput) + emission;

			lighting.rgb = ApplyVolumetricLight(lighting.rgb, input.position.xy, input.position.w);
			output.color.rgb = lighting;
			output.color.a = alpha;
		#endif
	#endif
	
	return output;
}
