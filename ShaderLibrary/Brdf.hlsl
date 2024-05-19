#ifndef BRDF_INCLUDED
#define BRDF_INCLUDED

#include "Math.hlsl"
#include "Utility.hlsl"
#include "Samplers.hlsl"

float4 _GGXDirectionalAlbedoRemap;
float2 _GGXAverageAlbedoRemap;
float2 _GGXDirectionalAlbedoMSScaleOffset;
float4 _GGXAverageAlbedoMSRemap;

Texture3D<float> _GGXDirectionalAlbedoMS;
Texture2D<float2> _GGXDirectionalAlbedo;
Texture2D<float> _GGXAverageAlbedo, _GGXAverageAlbedoMS;
Texture3D<float> _GGXSpecularOcclusion;

float Lambda(float NdotV, float a2)
{
	return sqrt(1.0 + a2 * (rcp(Sq(NdotV)) - 1.0)) * 0.5 - 0.5;
}

float G1(float NdotV, float a2)
{
	return rcp(1.0 + Lambda(NdotV, a2));
}

float G2(float NdotV, float NdotL, float a2)
{
	return rcp(1.0 + Lambda(NdotV, a2) + Lambda(NdotL, a2));
}

float3 F(float VdotH, float3 f0)
{
	return lerp(f0, 1.0, pow(1.0 - VdotH, 5.0));
}

float GGX_DV(float roughness, float NdotL, float NdotV, float NdotH)
{
	float a2 = Sq(roughness);
	float s = (NdotH * a2 - NdotH) * NdotH + 1.0;

	float lambdaV = NdotL * sqrt((-NdotV * a2 + NdotV) * NdotV + a2);
	float lambdaL = NdotV * sqrt((-NdotL * a2 + NdotL) * NdotL + a2);

	// This function is only used for direct lighting.
	// If roughness is 0, the probability of hitting a punctual or directional light is also 0.
	// Therefore, we return 0. The most efficient way to do it is with a max().
	return rcp(Pi) * 0.5 * a2 * rcp(max(Sq(s) * (lambdaV + lambdaL), FloatMin));
}

float F_Schlick(float f0, float u)
{
	return lerp(f0, 1.0, pow(1.0 - u, 5.0));
}

float3 F_Schlick(float3 f0, float u)
{
	return lerp(f0, 1.0, pow(1.0 - u, 5.0));
}

float D_GGXNoPI(float NdotH, float roughness)
{
	float a2 = Sq(roughness);
	float s = (NdotH * a2 - NdotH) * NdotH + 1.0;

    // If roughness is 0, returns (NdotH == 1 ? 1 : 0).
    // That is, it returns 1 for perfect mirror reflection, and 0 otherwise.
	return SafeDiv(a2, s * s);
}

float D_GGX(float NdotH, float roughness)
{
	return RcpPi * D_GGXNoPI(NdotH, roughness);
}

float3 GGX(float roughness, float3 specular, float VdotH, float NdotH, float NdotV, float NdotL)
{
	return F(VdotH, specular) * GGX_DV(roughness, NdotL, NdotV, NdotH);
}

// Note: V = G / (4 * NdotL * NdotV)
// Ref: http://jcgt.org/published/0003/02/03/paper.pdf
float V_SmithJointGGX(float NdotL, float NdotV, float roughness, float partLambdaV)
{
	float a2 = Sq(roughness);

    // Original formulation:
    // lambda_v = (-1 + sqrt(a2 * (1 - NdotL2) / NdotL2 + 1)) * 0.5
    // lambda_l = (-1 + sqrt(a2 * (1 - NdotV2) / NdotV2 + 1)) * 0.5
    // G        = 1 / (1 + lambda_v + lambda_l);

    // Reorder code to be more optimal:
	float lambdaV = NdotL * partLambdaV;
	float lambdaL = NdotV * sqrt((-NdotL * a2 + NdotL) * NdotL + a2);

    // Simplify visibility term: (2.0 * NdotL * NdotV) /  ((4.0 * NdotL * NdotV) * (lambda_v + lambda_l))
	return 0.5 / max(lambdaV + lambdaL, FloatMin);
}

// Precompute part of lambdaV
float GetSmithJointGGXPartLambdaV(float NdotV, float roughness)
{
	float a2 = Sq(roughness);
	return sqrt((-NdotV * a2 + NdotV) * NdotV + a2);
}

float V_SmithJointGGX(float NdotL, float NdotV, float roughness)
{
	float partLambdaV = GetSmithJointGGXPartLambdaV(NdotV, roughness);
	return V_SmithJointGGX(NdotL, NdotV, roughness, partLambdaV);
}

float3 AverageFresnel(float3 f0)
{
	return rcp(21.0) + 20 * rcp(21.0) * f0;
}

float2 DirectionalAlbedo(float NdotV, float perceptualRoughness)
{
	float2 uv = float2(sqrt(NdotV), perceptualRoughness) * _GGXDirectionalAlbedoRemap.xy + _GGXDirectionalAlbedoRemap.zw;
	return _GGXDirectionalAlbedo.SampleLevel(_LinearClampSampler, uv, 0);
}

float AverageAlbedo(float perceptualRoughness)
{
	float2 averageUv = float2(perceptualRoughness * _GGXAverageAlbedoRemap.x + _GGXAverageAlbedoRemap.y, 0.0);
	return _GGXAverageAlbedo.SampleLevel(_LinearClampSampler, averageUv, 0.0);
}

float DirectionalAlbedoMs(float NdotV, float perceptualRoughness, float3 f0)
{
	float3 uv = float3(sqrt(NdotV), perceptualRoughness, Max3(f0)) * _GGXDirectionalAlbedoMSScaleOffset.x + _GGXDirectionalAlbedoMSScaleOffset.y;
	return _GGXDirectionalAlbedoMS.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float AverageAlbedoMs(float perceptualRoughness, float3 f0)
{
	float2 uv = float2(perceptualRoughness, Max3(f0)) * _GGXAverageAlbedoMSRemap.xy + _GGXAverageAlbedoMSRemap.zw;
	return _GGXAverageAlbedoMS.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float3 GGXMultiScatter(float NdotV, float NdotL, float perceptualRoughness, float3 f0)
{
	float Ewi = DirectionalAlbedo(NdotV, perceptualRoughness).g;
	float Ewo = DirectionalAlbedo(NdotL, perceptualRoughness).g;
	float Eavg = AverageAlbedo(perceptualRoughness);
	float3 FAvg = AverageFresnel(f0);
	
	float ms = RcpPi * (1.0 - Ewi) * (1.0 - Ewo) * rcp(max(HalfEps, 1.0 - Eavg));
	float3 f = Sq(FAvg) * Eavg * rcp(max(HalfEps, 1.0 - FAvg * (1.0 - Eavg)));
	return ms * f;
}

float GGXDiffuse(float NdotL, float NdotV, float perceptualRoughness, float3 f0)
{
	float Ewi = DirectionalAlbedoMs(NdotL, perceptualRoughness, f0);
	float Ewo = DirectionalAlbedoMs(NdotV, perceptualRoughness, f0);
	float Eavg = AverageAlbedoMs(perceptualRoughness, f0);
	return (1.0 - Eavg) ? RcpPi * (1.0 - Ewo) * (1.0 - Ewi) * rcp(1.0 - Eavg) : 0.0;
}

#endif