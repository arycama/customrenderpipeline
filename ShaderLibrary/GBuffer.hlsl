#pragma once

#include "Common.hlsl"
#include "Color.hlsl"
#include "Material.hlsl"
#include "Packing.hlsl"
#include "Utility.hlsl"

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

float3 PackAlbedo(float3 rgb, float2 screenPosition)
{
	//return rgb;
	float3 yCoCg = RgbToYCoCg(rgb);
	
	uint2 interleavedPosition = (uint2) screenPosition % 2u;
	bool checkerboard = interleavedPosition.x == interleavedPosition.y;
	return float3(checkerboard ? yCoCg.xy : yCoCg.xz, 0);
}

float3 UnpackAlbedo(float4 enc, float2 screenPosition)
{
	//return enc.rgb;
	float2 a0 = enc.xy;
	float2 a1 = GbufferAlbedoMetallic[screenPosition + int2(1, 0)];
	float2 a2 = GbufferAlbedoMetallic[screenPosition + int2(-1, 0)];
	float2 a3 = GbufferAlbedoMetallic[screenPosition + int2(0, 1)];
	float2 a4 = GbufferAlbedoMetallic[screenPosition + int2(0, -1)];
	
	float threshold = 30.0 / 255.0;
	float4 lum = float4(a1.x, a2.x, a3.x, a4.x);
	float4 w = 1.0 - step(threshold, abs(lum - a0.x));
	float W = w.x + w.y + w.z + w.w;
	// handle the special case where all the weights are zero
	w.x = (W == 0.0) ? 1.0 : w.x;
	W = (W == 0.0) ? 1.0 : W;
	float chroma = (w.x * a1.y + w.y * a2.y + w.z * a3.y + w.w * a4.y) / W;

	uint2 screenXY = screenPosition.xy;
	bool pattern = (screenXY.x % 2) == (screenXY.y % 2);
	
	float3 yCoCg = pattern ? float3(enc.rg, chroma) : float3(enc.r, chroma, enc.g);
	
	//float2 offset = QuadOffset(screenPosition);
	//float4 horizontal = QuadReadAcrossX(enc, screenPosition);
	//float4 vertical = QuadReadAcrossY(enc, screenPosition);
	//float4 diagonal = QuadReadAcrossDiagonal(enc, screenPosition);
	
	//uint2 interleavedPosition = (uint2) screenPosition % 2u;
	//bool checkerboard = interleavedPosition.x == interleavedPosition.y;
	
	//float3 yCoCg = enc.rgb;
	//if (checkerboard)
	//	yCoCg.g = horizontal.g;
	//else
	//	yCoCg.b = horizontal.g;
		
	return YCoCgToRgb(yCoCg);
}

GBufferOutput OutputGBuffer(float3 albedo, float metallic, float3 normal, float perceptualRoughness, float3 bentNormal, float visibilityAngle, float3 emissive, float3 translucency, float2 screenPosition)
{
	albedo = PackAlbedo(albedo, screenPosition);

	GBufferOutput gbuffer;
	gbuffer.albedoMetallic = float4(albedo, metallic);
	gbuffer.normalRoughness = float4(PackGBufferNormal(normal), perceptualRoughness);
	gbuffer.bentNormalOcclusion = float4(PackGBufferNormal(bentNormal), visibilityAngle);
	gbuffer.emissive = emissive;
	gbuffer.translucency = float4(translucency, 1.0);
	return gbuffer;
}