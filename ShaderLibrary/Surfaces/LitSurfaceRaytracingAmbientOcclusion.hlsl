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
void RayTracing(inout RayPayloadAmbientOcclusion payload : SV_RayPayload, AttributeData attribs : SV_IntersectionAttributes)
{
	payload.hitDistance = RayTCurrent();
}