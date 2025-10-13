#pragma once

#include "Common.hlsl"
#include "Material.hlsl"
#include "Packing.hlsl"

struct GBufferOutput
{
	float4 albedoMetallic : SV_Target0;
	float4 normalRoughness : SV_Target1;
	float4 bentNormalOcclusion : SV_Target2;
	float3 emissive : SV_Target3;
	float4 translucency : SV_Target4;
};

Texture2D<float4> GbufferAlbedoMetallic, NormalRoughness, BentNormalOcclusion;

float3 PackGBufferNormal(float3 normal)
{
	return PackFloat2To888(NormalToOctahedralUv(normal));
}

float3 UnpackGBufferNormal(float4 data)
{
	return OctahedralUvToNormal(Unpack888ToFloat2(data.xyz));
}

float3 GBufferNormal(float4 data, float3 V, out float NdotV)
{
	float3 N = UnpackGBufferNormal(data);
	return GetViewClampedNormal(N, V, NdotV);
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

float3 PackAlbedo(float3 rgb)
{
	// TODO: YCoCg
	return rgb;
}

float3 UnpackAlbedo(float4 enc)
{
	// TODO: YCoCg
	return enc.rgb;
}

GBufferOutput OutputGBuffer(float3 albedo, float metallic, float3 normal, float perceptualRoughness, float3 bentNormal, float visibilityAngle, float3 emissive, float3 translucency)
{
	albedo = PackAlbedo(albedo);

	GBufferOutput gbuffer;
	gbuffer.albedoMetallic = float4(albedo, metallic);
	gbuffer.normalRoughness = float4(PackGBufferNormal(normal), perceptualRoughness);
	gbuffer.bentNormalOcclusion = float4(PackGBufferNormal(bentNormal), visibilityAngle);
	gbuffer.emissive = emissive;
	gbuffer.translucency = float4(translucency, 1.0);
	return gbuffer;
}