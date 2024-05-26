#include "../Common.hlsl"
#include "../Exposure.hlsl"
#include "../GBuffer.hlsl"
#include "../Lighting.hlsl"
#include "../Material.hlsl"
#include "../Samplers.hlsl"
#include "../VolumetricLight.hlsl"
#include "../Temporal.hlsl"
#include "../Random.hlsl"
#include "../Raytracing.hlsl"
#include "../RaytracingLighting.hlsl"

Texture2D<float4> _BentNormal, _MainTex, _BumpMap, _MetallicGlossMap, _DetailAlbedoMap, _DetailNormalMap, _OcclusionMap, _ParallaxMap;
Texture2D<float3> _EmissionMap;
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

[shader("closesthit")]
void RayTracing(inout RayPayload payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
{
	MeshInfo meshInfo = unity_MeshInfo_RT[0];
	if(meshInfo.indexSize != 2)
		return;
	
	uint index = PrimitiveIndex();
    uint3 triangleIndices = UnityRayTracingFetchTriangleIndices(index);
	
	Vert v0, v1, v2;
	v0 = FetchVertex(triangleIndices.x);
	v1 = FetchVertex(triangleIndices.y);
	v2 = FetchVertex(triangleIndices.z);

	float3 barycentricCoords = float3(1.0 - attribs.barycentrics.x - attribs.barycentrics.y, attribs.barycentrics.x, attribs.barycentrics.y);
	Vert v = InterpolateVertices(v0, v1, v2, barycentricCoords);
	
	float3 normal = MultiplyVector(v.normal, WorldToObject3x4(), true);
	float2 uv = v.uv;

	float4 albedoAlpha = _MainTex.SampleLevel(_LinearRepeatSampler, uv, 0.0);
	
	float3 emission = _EmissionMap.SampleLevel(_LinearRepeatSampler, uv, 0.0) * _EmissionColor;
	emission = lerp(emission, emission * _Exposure, _EmissiveExposureWeight);

	float3 worldPosition = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
	float3 V = -WorldRayDirection();
	
	float4 metallicGloss = _MetallicGlossMap.SampleLevel(_LinearRepeatSampler, uv, 0.0);
	float metallic = metallicGloss.r * _Metallic;

	float perceptualSmoothness;
	if(Smoothness_Source)
		perceptualSmoothness = albedoAlpha.a * _Smoothness;
	else
		perceptualSmoothness = metallicGloss.a * _Smoothness;
	
	float perceptualRoughness = PerceptualSmoothnessToPerceptualRoughness(perceptualSmoothness);
	
	float3 f0 = lerp(0.04, albedoAlpha.rgb, metallic);
	float occlusion = _OcclusionMap.SampleLevel(_LinearRepeatSampler, uv, 0.0).g;
	
	float3 color = RaytracedLighting(worldPosition, normal, V, f0, perceptualRoughness, occlusion, normal, albedoAlpha.rgb) + emission;
	
	payload.packedColor = Float3ToR11G11B10(color);
	payload.hitDistance = RayTCurrent();
}
