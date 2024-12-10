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

float GgxDistribution(float roughness, float NdotH)
{
	// Eq 31: https://dassaultsystemes-technology.github.io/EnterprisePBRShadingModel/spec-2025x.md.html
	float a2 = Sq(roughness);
	return RcpPi * a2 * rcp(Sq(Sq(NdotH) * (a2 - 1.0) + 1.0));
}

float Lambda(float NdotV, float roughness)
{
	// Eq 29: https://dassaultsystemes-technology.github.io/EnterprisePBRShadingModel/spec-2025x.md.html
	return (-1.0 + sqrt(1.0 + Sq(roughness) * (rcp(Sq(NdotV)) - 1.0))) / 2.0;
}

float GgxShadowingMasking(float roughness, float NdotV, float NdotL)
{
	// Eq 28: https://dassaultsystemes-technology.github.io/EnterprisePBRShadingModel/spec-2025x.md.html
	return rcp(1.0 + Lambda(NdotV, roughness) + Lambda(NdotL, roughness));
}

float GgxShadowingMasking(float roughness, float NdotV, float NdotL, float LdotH, float VdotH)
{
	// Eq 28: https://dassaultsystemes-technology.github.io/EnterprisePBRShadingModel/spec-2025x.md.html
	return (LdotH > 0 && VdotH > 0) ? GgxShadowingMasking(roughness, NdotV, NdotL) : 0.0;
}

// Ref: http://jcgt.org/published/0003/02/03/paper.pdf
float GgxVisibility(float roughness, float NdotL, float NdotV)
{
	float g = GgxShadowingMasking(roughness, NdotV, NdotL);
	
	// Eq 23: https://dassaultsystemes-technology.github.io/EnterprisePBRShadingModel/spec-2025x.md.html
	return g * rcp(4.0 * abs(NdotV) * abs(NdotL)); // Unsure about the abs
}

float GgxReflection(float roughness, float NdotL, float NdotV, float NdotH)
{
	float d = GgxDistribution(roughness, NdotH);
	float v = GgxVisibility(roughness, NdotV, NdotL);
	return d * v;
}

float GgxReflection(float roughness, float NdotL, float NdotV, float NdotH, float LdotH, float VdotH)
{
	if(LdotH <= 0.0 || VdotH <= 0.0 || NdotH <= 0.0)
		return 0.0;
		
	float d = GgxDistribution(roughness, NdotH);
	float v = GgxVisibility(roughness, NdotV, NdotL);
	return d * v;
}

float3 GgxTransmission(float roughness, float NdotL, float NdotV, float NdotHt, float LdotHt, float VdotHt, float3 ni, float3 no)
{
	float d = GgxDistribution(roughness, NdotHt);
	//float g = GgxShadowingMasking(roughness, NdotV, NdotL, LdotHt, VdotHt);
	float g = GgxShadowingMasking(roughness, NdotV, NdotL);
	return abs(LdotHt) * abs(VdotHt) * rcp(abs(NdotL) * abs(NdotV)) * (Sq(no) * d * g * rcp(Sq(ni * LdotHt + no * VdotHt)));
}


float Fresnel(float LdotH, float f0)
{
	return lerp(f0, 1.0, pow(1.0 - LdotH, 5.0));
}

float3 Fresnel(float LdotH, float3 f0)
{
	return lerp(f0, 1.0, pow(1.0 - LdotH, 5.0));
}

// Includes handling for TIR
float Fresnel(float LdotH, float ni, float no)
{
	if (ni > no)
	{
		float invEta = no / ni;
		float sinTheta2 = Sq(invEta) * (1.0 - Sq(LdotH));
	
		if (sinTheta2 > 1.0)
			return 1.0; // TIR
		
		LdotH = sqrt(1.0 - sinTheta2);
	}
	
	float f0 = Sq((ni - no) * rcp(ni + no));
	return Fresnel(LdotH, f0);
}

float3 AverageFresnel(float3 f0)
{
	return rcp(21.0) + 20 * rcp(21.0) * f0;
}

float2 DirectionalAlbedo(float NdotV, float perceptualRoughness)
{
	if(NdotV <= 0.0)
		return 0.0;
		
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
	if (NdotV <= 0 || NdotL <= 0.0)
		return 0.0;

	float Ewo = DirectionalAlbedo(NdotV, perceptualRoughness).g;
	float Ewi = DirectionalAlbedo(NdotL, perceptualRoughness).g;
	float Emavg = AverageAlbedo(perceptualRoughness);
	
	// Multiple-scattering ggx
	float ms = (1.0 - Ewo) * (1.0 - Ewi) * rcp(max(HalfEps, Pi * (1.0 - Emavg)));
	
	// Multiple-scattering Fresnsel
	float3 FAvg = AverageFresnel(f0);
	float3 f = Sq(FAvg) * Emavg * rcp(max(HalfEps, 1.0 - FAvg * (1.0 - Emavg)));
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