#pragma once

#include "Common.hlsl"
#include "Color.hlsl"
#include "Geometry.hlsl"
#include "Material.hlsl"
#include "Packing.hlsl"
#include "Utility.hlsl"

struct GBufferOutput
{
	float4 albedoMetallic : SV_Target0;
	float4 normalRoughness : SV_Target1;
	float4 bentNormalOcclusion : SV_Target2;
	
	#ifndef EMISSION_DISABLED
		float3 emissive : SV_Target3;
	#endif
};

Texture2D<float4> GBufferAlbedoMetallic, GBufferNormalRoughness, GBufferBentNormalOcclusion;

float2 PackGBufferNormal(float3 N, float3 V, float3x3 worldToView)
{
	N = mul(worldToView, N);
	V = mul(worldToView, V);
	return NormalToHemiOctahedralUv(FromToRotationZInverse(-V, -N));
}

float3 UnpackGBufferNormal(float4 data, float3 V, float3x3 ViewToWorld, float3x3 worldToView)
{
	V = mul(worldToView, V);
	float3 N = HemiOctahedralUvToNormal(data.xy);
	return mul(ViewToWorld, normalize(FromToRotationZ(-V, -N)));
}

float3 GBufferNormal(float4 data, float3 V, out float NdotV, float3x3 ViewToWorld, float3x3 worldToView)
{
	float3 N = UnpackGBufferNormal(data, V, ViewToWorld, worldToView);
	return GetViewClampedNormal(N, V, NdotV);
}

float3 GBufferNormal(float4 data, float3 V, float3x3 ViewToWorld, float3x3 worldToView)
{
	float NdotV;
	return GBufferNormal(data, V, NdotV, ViewToWorld, worldToView);
}

float3 GBufferNormal(uint2 coord, Texture2D<float4> tex, float3 V, out float NdotV, float3x3 ViewToWorld, float3x3 worldToView)
{
	return GBufferNormal(tex[coord], V, NdotV, ViewToWorld, worldToView);
}

float3 GBufferNormal(uint2 coord, Texture2D<float4> tex, float3 V, float3x3 ViewToWorld, float3x3 worldToView)
{
	float NdotV;
	return GBufferNormal(coord, tex, V, NdotV, ViewToWorld, worldToView);
}

float2 PackAlbedo(float3 rgb, float2 screenPosition)
{
	float3 yCoCg = RgbToYCoCg(rgb);
	return Checker(screenPosition) ? yCoCg.xy : yCoCg.xz;
}

float3 UnpackAlbedo(float2 enc, float2 screenPosition, float2 a0, float2 a1)
{
	float threshold = 30.0 / 255.0;
	float2 lum = float2(a0.x, a1.x);
	float2 w = 1.0 - step(threshold, abs(lum - enc.x));
	float W = w.x + w.y;
	
	// handle the special case where all the weights are zero
	w.x = (W == 0.0) ? 1.0 : w.x;
	W = (W == 0.0) ? 1.0 : W;
	float chroma = (w.x * a0.y + w.y * a1.y) / W;

	float3 yCoCg = Checker(screenPosition) ? float3(enc.rg, chroma) : float3(enc.r, chroma, enc.g);
	return YCoCgToRgb(yCoCg);
}

float3 UnpackAlbedo(float2 enc, float2 screenPosition)
{
	float2 a0 = QuadReadAcrossX(enc, screenPosition);
	float2 a1 = QuadReadAcrossY(enc, screenPosition);
	return UnpackAlbedo(enc, screenPosition, a0, a1);
}

GBufferOutput OutputGBuffer(float3 albedo, float metallic, float3 normal, float perceptualRoughness, float3 bentNormal, float visibilityAngle, float3 emissive, float3 translucency, float2 screenPosition, float3 V, bool isTranslucent, float3x3 worldToView)
{
	GBufferOutput gbuffer;
	gbuffer.albedoMetallic = float4(PackAlbedo(albedo, screenPosition), isTranslucent ? PackAlbedo(translucency, screenPosition) : float2(0, metallic));
	gbuffer.normalRoughness = float4(PackGBufferNormal(normal, V, worldToView), perceptualRoughness, 0);
	gbuffer.bentNormalOcclusion = float4(PackGBufferNormal(bentNormal, V, worldToView), visibilityAngle, 0);
	
	#ifndef EMISSION_DISABLED
		gbuffer.emissive = emissive;
	#endif
	
	return gbuffer;
}