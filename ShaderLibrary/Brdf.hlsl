#ifndef BRDF_INCLUDED
#define BRDF_INCLUDED

#include "Material.hlsl"
#include "Math.hlsl"
#include "Samplers.hlsl"

Texture2D<float> DirectionalAlbedo, AverageAlbedo, AverageAlbedoMs;

float LambdaGgx(float roughness2, float cosTheta)
{
	return sqrt((Sq(rcp(cosTheta)) - 1.0) * roughness2 + 1.0) * 0.5 - 0.5;
}

float GgxG1(float a2, float NdotL, float LdotH)
{
	float cosThetaV2 = Sq(NdotL);
	float tanThetaV2 = (1.0 - cosThetaV2) / cosThetaV2;
	//return 2 / (1 + sqrt(1.0 + a2 * tanThetaV2));
	return ((LdotH * NdotL) > 0) ? 2 / (1 + sqrt(1.0 + a2 * tanThetaV2)) : 0;
}

float GgxG2(float roughness2, float cosThetaI, float cosThetaO)
{
	return rcp(1.0 + LambdaGgx(roughness2, cosThetaI) + LambdaGgx(roughness2, cosThetaO));
}

half GetPartLambdaV(half roughness2, half NdotV)
{
	return sqrt((-NdotV * roughness2 + NdotV) * NdotV + roughness2);
}

float GgxDistribution(float roughness2, float NdotH)
{
	float denom = Sq((NdotH * roughness2 - NdotH) * NdotH + 1.0);
	return roughness2 ? (denom ? roughness2 * rcp(denom) : 0.0) : (NdotH == 1);
}

float GgxD(float a2, float NdotH)
{
	return (NdotH > 0.0) ? a2 / (Pi * Sq((a2 - 1) * Sq(NdotH) + 1.0)) : 0.0;
}

float GgxG(float roughness2, half NdotL, half LdotH, half NdotV, half VdotH)
{
	return GgxG1(roughness2, NdotL, LdotH) * GgxG1(roughness2, NdotV, VdotH);
}

float GgxV(float NdotL, float NdotV, float roughness2, float partLambdaV)
{
	float lambdaV = NdotL * partLambdaV;
	float lambdaL = NdotV * GetPartLambdaV(roughness2, NdotL);
	return 0.5 * rcp(lambdaV + lambdaL);
}

float GgxDv(float roughness2, float NdotH, float NdotL, float NdotV, float partLambdaV)
{
	float s2 = Sq((NdotH * roughness2 - NdotH) * NdotH + 1.0);
	float lambdaL = NdotV * GetPartLambdaV(roughness2, NdotL);
	float denom = TwoPi * (NdotL * partLambdaV + lambdaL) * s2;
	return denom ? roughness2 * rcp(denom) : 0.0;
}

half3 FresnelFull(half c, half3 iorRatio)
{
	half3 g = sqrt(Sq(iorRatio) - 1.0h + Sq(c));
	return 0.5h * (Sq(g - c) / Sq(g + c)) * (1.0h + Sq(c * (g + c) - 1.0h) / Sq(c * (g - c) + 1.0h));
}

half FresnelTerm(half LdotH)
{
	return pow(1.0h - LdotH, 5.0h);
}

half3 Fresnel(half LdotH, half3 reflectivity)
{
	return lerp(reflectivity, 1.0h, FresnelTerm(LdotH));
}

half FresnelTir(half LdotH, half reflectivity)
{
	half sinThetaSq = Sq(ReflectivityToRcpIorRatio(reflectivity).r) * (1.0h - Sq(LdotH));
	LdotH = reflectivity < 0.0h ? sqrt(1.0h - sinThetaSq) : LdotH;
	return sinThetaSq < 1.0h ? Fresnel(LdotH, reflectivity).r : 1.0h;
}

float3 GgxSingleScatter(float roughness2, float NdotL, float LdotV, float NdotV, float partLambdaV, float3 f0)
{
	float rcpLenLv = rsqrt(2.0 + 2.0 * LdotV);
	float NdotH = (NdotL + NdotV) * rcpLenLv;
	float ggx = GgxDv(roughness2, NdotH, NdotL, NdotV, partLambdaV);
	float LdotH = LdotV * rcpLenLv + rcpLenLv;
	return ggx * Fresnel(LdotH, f0);
}

half3 AverageFresnel(half3 reflectivity)
{
	return (20 * rcp(21.0)) * reflectivity + rcp(21.0);
}

half AverageFresnel(half reflectivity)
{
	return AverageFresnel(reflectivity).r;
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

half WrappedDiffuse(half NdotL, half wrap)
{
	return saturate((NdotL + wrap) / (Sq(1 + wrap)));
}

half3 GgxBrdf(half a2, half3 reflectivity, half3 N, half3 L, half NdotL, half3 V, half NdotV, half opacity = 1.0h, bool isBackfacing = false)
{
	if (isBackfacing)
	{
		L = reflect(L, -N);
		NdotL = -NdotL;
	}
	
	half3 H = normalize(L + V);
	half LdotV = dot(L, V);
	half NdotH = dot(N, H);
	half LdotH = dot(L, H);
	half VdotH = dot(V, H);
		
	half dv = GgxDv(a2, NdotH, NdotL, NdotV, GetPartLambdaV(a2, NdotV));
	half3 F = Fresnel(LdotH, reflectivity);
	
	if (isBackfacing)
		F = 1.0 - F;
	
	half3 specular = dv * F * NdotL;
		
	if (isBackfacing)
		specular *= 1.0 - opacity;
			
	return specular;
}

half3 GgxBtdf(half a2, half3 N, half NdotV, half3 L, half3 V, half reflectivity, half NdotL)
{
	half iorRatio = ReflectivityToIorRatio(reflectivity).r;
	
	half LdotV = dot(L, V);
	half rcpDenominator = rsqrt(1.0h + 2.0h * iorRatio * LdotV + Sq(iorRatio)); // Can this be combined with rcpLenLv
	half NdotH = SignFlip((NdotL + iorRatio * NdotV), -reflectivity) * rcpDenominator;
	half VdotH = -SignFlip(iorRatio + LdotV, -reflectivity) * rcpDenominator;
	half LdotH = SignFlip(1.0h + iorRatio * LdotV, -reflectivity) * rcpDenominator;

	if (NdotH <= 0.0 || LdotH <= 0.0 || NdotH <= 0.0)
		return 0;
		
	half dv = GgxDv(a2, NdotH, NdotL, -NdotV, GetPartLambdaV(a2, abs(NdotV)));
	half f = 1.0 - Fresnel(LdotH, reflectivity);
	return f * dv * 4.0h * LdotH * VdotH * Sq(ReflectivityToIor(reflectivity)) * rcp(Sq(LdotH + iorRatio * VdotH));
}

#endif