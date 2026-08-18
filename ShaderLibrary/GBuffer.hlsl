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

// Assumes N and V are already in view space
float2 PackGBufferNormal(float3 N, float3 V)
{
	N = FromToRotationZInverse(-V, -N, false);
	return NormalToPyramidUv(N);
}

// Assumes N and V are in world space
float2 PackGBufferNormal(float3 N, float3 V, matrix worldToView)
{
	V = mul((float3x3) worldToView, V);
	N = mul((float3x3) worldToView, N);
	return PackGBufferNormal(N, V);
}

// Returns N in view space, assumes V is in viewspace
float3 GBufferNormal(float4 data, float3 V, out float NdotV)
{
	float3 N = PyramidUvToNormal(data.rg);
	NdotV = N.z;
	return -FromToRotationZ(-V, N, false);
}

// Returns N in world space, assumes V is in world space
float3 GBufferNormal(float4 data, float3 V, out float NdotV, matrix worldToView, matrix viewToWorld)
{
	V = mul((float3x3) worldToView, V);
	float3 N = GBufferNormal(data, V, NdotV);
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

float2 PackAlbedo(float3 c, half2 screenPosition)
{
    // Scale to integer ranges (5-6-5 bits) and round
	float r = floor(c.r * 31.0f + 0.5f);
	float g = floor(c.g * 63.0f + 0.5f);
	float b = floor(c.b * 31.0f + 0.5f);
    
    // Pack into a single float (equivalent to bits: rrrrrggggggbbbbb)
	float packed16 = (r * 2048.0f) + (g * 32.0f) + b;
    
    // Split into high byte (bits 8-15) and low byte (bits 0-7)
	float highByte = floor(packed16 / 256.0f);
	float lowByte = packed16 - (highByte * 256.0f);
    
    // Normalise to 0.0 - 1.0 for standard 8-bit target output
	return float2(highByte / 255.0f, lowByte / 255.0f);
	
	//half3 yCoCg = RgbToYCbCr(rgb);
	//return Checker(screenPosition) ? yCoCg.xy : yCoCg.xz;
}

half3 UnpackAlbedo(half2 packedFloat2, half2 screenPosition, half2 a0, half2 a1)
{
   // Reconstruct the 0-255 byte values
	float highByte = floor(saturate(packedFloat2.x) * 255.0f + 0.5f);
	float lowByte = floor(saturate(packedFloat2.y) * 255.0f + 0.5f);
    
    // Combine back into the single 16-bit floating point value
	float packed16 = (highByte * 256.0f) + lowByte;
    
    // Extract Red (top 5 bits)
	float r = floor(packed16 / 2048.0f);
	packed16 -= r * 2048.0f;
    
    // Extract Green (middle 6 bits)
	float g = floor(packed16 / 32.0f);
	packed16 -= g * 32.0f;
    
    // Extract Blue (bottom 5 bits)
	float b = packed16;
    
    // Rescale back to 0.0 - 1.0 range
	return float3(r / 31.0f, g / 63.0f, b / 31.0f);
	
	//half2 lum = half2(a0.x, a1.x);
	//half2 w = 1.0h - saturate((abs(lum - enc.x) - 30.0h / 255.0h) * HalfMax);
	//half W = w.x + w.y;
	//half coCg = W ? (w.x * a0.y + w.y * a1.y) / W : a0.y;
	
	//half3 yCoCg = half3(enc, coCg);
	
	//if (!Checker(screenPosition))
	//	yCoCg.yz = yCoCg.zy;
		
	//return YCbCrToRgb(yCoCg);
}

half3 UnpackAlbedo(half2 enc, half2 screenPosition)
{
	half2 a0 = QuadReadAcrossX(enc, screenPosition);
	half2 a1 = QuadReadAcrossY(enc, screenPosition);
	return UnpackAlbedo(enc, screenPosition, a0, a1);
}

GBufferOutput OutputGBuffer(float3 albedo, float metallic, float3 normal, float roughness, float3 bentNormal, float cosVisibilityAngle, float3 emissive, float translucency, float2 screenPosition, float3 V)
{
	GBufferOutput gbuffer;
	gbuffer.albedoMetallic = float4(PackAlbedo(albedo, screenPosition), translucency, metallic);
	gbuffer.normalRoughness = float4(PackGBufferNormal(normal, V), roughness, 0);
	gbuffer.bentNormalOcclusion = float4(PackGBufferNormal(bentNormal, V), cosVisibilityAngle, 0);
	
	#ifndef EMISSION_DISABLED
		gbuffer.emissive = emissive;
	#endif
	
	return gbuffer;
}

GBufferOutput OutputGBuffer(float3 albedo, float metallic, float3 normal, float roughness, float3 bentNormal, float cosVisibilityAngle, float3 emissive, float translucency, float2 screenPosition, float3 V, matrix worldToView)
{
	V = mul((float3x3) worldToView, V);
	normal = mul((float3x3) worldToView, normal);
	bentNormal = mul((float3x3) worldToView, bentNormal);

	return OutputGBuffer(albedo, metallic, normal, roughness, bentNormal, cosVisibilityAngle, emissive, translucency, screenPosition, V);
}

GBufferOutput OutputGBuffer(Material material, float2 screenPosition, float3 V, matrix worldToView)
{
	return OutputGBuffer(material.albedo, material.metallic, material.normal, material.roughness, material.bentNormal, material.occlusion, material.emission, material.translucency, screenPosition, V, worldToView);
}