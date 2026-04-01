#pragma once

#include "Color.hlsl"
#include "Geometry.hlsl"
#include "Material.hlsl"
#include "Packing.hlsl"
#include "Random.hlsl"
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

float2 PackGBufferNormal(float3 N, float3 V, matrix worldToView)
{
	V = mul((float3x3) worldToView, V);
	N = mul((float3x3) worldToView, N);
	N = FromToRotationZInverse(-V, -N);
	return NormalToPyramidUv(N);
}

float3 GBufferNormal(float4 data, float3 V, out float NdotV, matrix worldToView, matrix viewToWorld)
{
	float3 N = PyramidUvToNormal(data.rg);
	NdotV = N.z;
	V = mul((float3x3) worldToView, V);
	N = -FromToRotationZ(-V, N);
	return mul((float3x3) viewToWorld, N);
}

float3 GBufferNormal(float4 data, float3 V, matrix worldToView, matrix viewToWorld)
{
	float NdotV;
	return GBufferNormal(data, V, NdotV, worldToView, viewToWorld);
}

float3 GBufferNormal(uint2 coord, Texture2D<float4> tex, float3 V, out float NdotV, matrix worldToView, matrix viewToWorld)
{
	return GBufferNormal(tex[coord], V, NdotV, worldToView, viewToWorld);
}

float3 GBufferNormal(uint2 coord, Texture2D<float4> tex, float3 V, matrix worldToView, matrix viewToWorld)
{
	float NdotV;
	return GBufferNormal(coord, tex, V, NdotV, worldToView, viewToWorld);
}

half2 PackAlbedo(half3 rgb, float2 screenPosition)
{
	half3 yCoCg = RgbToYCbCr(rgb);
	return Checker(screenPosition) ? yCoCg.xy : yCoCg.xz;
}

half3 UnpackAlbedo(half2 enc, float2 screenPosition, half2 a0, half2 a1)
{
	half2 lum = half2(a0.x, a1.x);
	half2 w = 1.0h - saturate((abs(lum - enc.x) - 30.0h / 255.0h) * HalfMax);
	half W = w.x + w.y;
	half coCg = W ? (w.x * a0.y + w.y * a1.y) / W : a0.y;
	
	half3 yCoCg = half3(enc, coCg);
	if (!Checker(screenPosition))
		yCoCg.yz = yCoCg.zy;
		
	return YCbCrToRgb(yCoCg);
}

float3 UnpackAlbedo(float2 enc, float2 screenPosition)
{
	float2 a0 = QuadReadAcrossX(enc, screenPosition);
	float2 a1 = QuadReadAcrossY(enc, screenPosition);
	return UnpackAlbedo(enc, screenPosition, a0, a1);
}

GBufferOutput OutputGBuffer(float3 albedo, float metallic, float3 normal, float perceptualRoughness, float3 bentNormal, float visibilityAngle, float3 emissive, float translucency, float2 screenPosition, float3 V, matrix worldToView)
{
	GBufferOutput gbuffer;
	gbuffer.albedoMetallic = float4(PackAlbedo(albedo, screenPosition), translucency, metallic);
	gbuffer.normalRoughness = float4(PackGBufferNormal(normal, V, worldToView), perceptualRoughness, 0);
	gbuffer.bentNormalOcclusion = float4(PackGBufferNormal(bentNormal, V, worldToView), visibilityAngle, 0);
	
	#ifndef EMISSION_DISABLED
		gbuffer.emissive = emissive;
	#endif
	
	return gbuffer;
}

GBufferOutput OutputGBuffer(Material material, float2 screenPosition, float3 V, matrix worldToView)
{
	GBufferOutput gbuffer;
	gbuffer.albedoMetallic = float4(PackAlbedo(material.albedo, screenPosition), material.translucency, material.metallic);
	gbuffer.normalRoughness = float4(PackGBufferNormal(material.normal, V, worldToView), material.roughness, 0);
	gbuffer.bentNormalOcclusion = float4(PackGBufferNormal(material.bentNormal, V, worldToView), material.cosVisibilityAngle, 0);
	
	#ifndef EMISSION_DISABLED
		gbuffer.emissive = material.emission;
	#endif
	
	return gbuffer;
}