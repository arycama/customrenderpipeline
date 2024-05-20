#ifndef GBUFFER_INCLUDED
#define GBUFFER_INCLUDED

#include "Packing.hlsl"
#include "Lighting.hlsl"

float3 GBufferNormal(float4 data, float3 V, out float NdotV)
{
	float3 N = UnpackNormalOctQuadEncode(2.0 * Unpack888ToFloat2(data.xyz) - 1.0);
	return GetViewReflectedNormal(N, V, NdotV);
}

float3 GBufferNormal(float4 data, float3 V)
{
	float NdotV;
	return GBufferNormal(data, V, NdotV);
}

float3 GBufferNormal(uint2 coord, Texture2D<float4> tex, float3 V, out float NdotV)
{
	return GBufferNormal(tex[coord], V, NdotV);
}

float3 GBufferNormal(uint2 coord, Texture2D<float4> tex, float3 V)
{
	float NdotV;
	return GBufferNormal(coord, tex, V, NdotV);
}

struct GBufferOutput
{
	float4 albedoMetallic : SV_Target0;
	float4 normalRoughness : SV_Target1;
	float4 bentNormalOcclusion : SV_Target2;
	float3 emissive : SV_Target3;
};

GBufferOutput OutputGBuffer(float3 albedo, float metallic, float3 normal, float perceptualRoughness, float3 bentNormal, float occlusion, float3 emissive)
{
	GBufferOutput gbuffer;
	gbuffer.albedoMetallic = float4(albedo, metallic);
	gbuffer.normalRoughness = float4(PackFloat2To888(0.5 * PackNormalOctQuadEncode(normal) + 0.5), perceptualRoughness);
	gbuffer.bentNormalOcclusion = float4(bentNormal * 0.5 + 0.5, occlusion);
	gbuffer.emissive = emissive;
	return gbuffer;
}

#endif