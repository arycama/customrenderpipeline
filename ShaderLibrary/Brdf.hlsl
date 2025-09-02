#pragma once

#include "Common.hlsl"
#include "Material.hlsl"
#include "Math.hlsl"
#include "Samplers.hlsl"

Texture2D<float> DirectionalAlbedo, AverageAlbedo, AverageAlbedoMs;
Texture3D<float> DirectionalAlbedoMs;

float GetPartLambdaV(float roughness2, float NdotV)
{
	return sqrt((-NdotV * roughness2 + NdotV) * NdotV + roughness2);
}

float GgxDistribution(float roughness2, float NdotH)
{
	float denom = Sq((NdotH * roughness2 - NdotH) * NdotH + 1.0);
	return roughness2 ? (denom ? roughness2 * rcp(denom) : 0.0) : (NdotH == 1);
}

float GgxV(float NdotL, float NdotV, float roughness2, float partLambdaV)
{
	float lambdaV = NdotL * partLambdaV;
	float lambdaL = NdotV * sqrt((-NdotL * roughness2 + NdotL) * NdotL + roughness2);
	return 0.5 * rcp(lambdaV + lambdaL);
}

float GgxDv(float roughness2, float NdotH, float NdotL, float NdotV, float partLambdaV)
{
	float s2 = Sq((NdotH * roughness2 - NdotH) * NdotH + 1.0);
	float lambdaL = NdotV * sqrt((-NdotL * roughness2 + NdotL) * NdotL + roughness2);
	float denom = 2.0 * (NdotL * partLambdaV + lambdaL) * s2;
	return denom ? roughness2 * rcp(denom) : 0.0;
}

float FresnelTerm(float LdotH)
{
	return pow(1.0 - LdotH, 5.0);
}

float3 Fresnel(float LdotH, float3 f0)
{
	return lerp(f0, 1.0, FresnelTerm(LdotH));
}

float3 GgxSingleScatter(float roughness2, float NdotL, float LdotV, float NdotV, float partLambdaV, float3 f0)
{
	float rcpLenLv = rsqrt(2.0 + 2.0 * LdotV);
	float NdotH = (NdotL + NdotV) * rcpLenLv;
	float ggx = GgxDv(roughness2, NdotH, NdotL, NdotV, partLambdaV);
	float LdotH = LdotV * rcpLenLv + rcpLenLv;
	return ggx * Fresnel(LdotH, f0);
}

float AverageFresnel(float f0)
{
	return (20 * rcp(21.0)) * f0 + rcp(21.0);
}

float3 AverageFresnel(float3 f0)
{
	return (20 * rcp(21.0)) * f0 + rcp(21.0);
}

float3 GgxMultiScatterTerm(float3 f0, float perceptualRoughness, float NdotV, float ems)
{
	float averageAlbedo = AverageAlbedo.Sample(LinearClampSampler, Remap01ToHalfTexel(float2(perceptualRoughness, 0), float2(32, 1)));
	float3 averageFresnel = AverageFresnel(f0);
	float3 denominator = averageAlbedo - averageFresnel * Sq(averageAlbedo);
	
	// AverageAlbedo for NdotL is already applied to each light contribution
	return denominator ? (ems * Sq(averageFresnel) * (1.0 - averageAlbedo) * rcp(denominator)) : 0.0;
}

float3 Ggx(float roughness2, float NdotL, float LdotV, float NdotV, float partLambdaV, float perceptualRoughness, float3 f0, float3 multiScatterTerm)
{
	// TODO: Maybe can combine 2nd lookup with diffuse multi scatter LUT
	return GgxSingleScatter(roughness2, NdotL, LdotV, NdotV, partLambdaV, f0) + DirectionalAlbedo.SampleLevel(LinearClampSampler, Remap01ToHalfTexel(float2(NdotL, perceptualRoughness), 32), 0.0) * multiScatterTerm;
}

//float4 BrdfDirect(float NdotL, float perceptualRoughness, float f0Avg, float roughness2, float LdotV, float NdotV, float partLambdaV)
//{
//	float diffuse = DirectionalAlbedoMs.Sample(LinearClampSampler, Remap01ToHalfTexel(float3(NdotL, perceptualRoughness, f0Avg), 16));
//	float3 specular = Ggx(roughness2, NdotL, LdotV, NdotV, partLambdaV, perceptualRoughness);
//	return float4(specular, diffuse) * NdotL; // RcpPi is multiplied outside of this function
//}
