#pragma once

#include "Math.hlsl"

// All the non cosine zonal harmonics are divided by 4, since they are used for volumetric functions which need to be divided by four pi.
const static float3 IsotropicZonalHarmonics = float3(1.0, 0.0, 0.0) / 4.0;
const static float3 RayleighZonalHarmonics = float3(1.0, 0.0, 0.5) / 4.0;
const static float3 HazyZonalHarmonics = float3(1.0, 0.9, 0.8) / 4.0;
const static float3 MurkyZonalHarmonics = float3(1.0, 0.95, 0.9) / 4.0;

float3 SchlickZonalHarmonics(float g)
{
	return float3(1.0, g, g * g) / 4.0;
}

float3 HenyeyGreensteinZonalHarmonics(float g)
{
	return float3(1.0, g, g * g) / 4.0;
}

float3 CornetteShanksZonalHarmonics(float g)
{
	return float3(1.0, g, 0.5 * (3.0 * Sq(g) - 1.0)) / 4.0;
}

float3 CosineZonalHarmonics(float visibilityAperture)
{
	float3 zonalHarmonics = float3(1.0, 2.0 / 3.0, 0.25);
	
	// Eq 23: https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion_NEW%20VERSION_COLOR.pdf
	float a = saturate(sin(visibilityAperture));
	float b = saturate(cos(visibilityAperture)); // Some weird cases can cause this to go slightly negative
	
	// Calculate the zonal harmonics expansion for V(x, ?i)*(n.l)
	zonalHarmonics.x *= Sq(a);
	zonalHarmonics.y *= 1.0 - pow(b, 3.0);
	zonalHarmonics.z *= Sq(a) + Sq(a) * 3.0 * Sq(b);
	return zonalHarmonics;
}

float3 EvaluateSh(float3 N, float4 sh[9], float3 zh)
{
	float3 irradiance = 0.0;
	irradiance.r = dot(sh[0].xyz * zh.y, N) + sh[0].w * zh.x;
	irradiance.g = dot(sh[1].xyz * zh.y, N) + sh[1].w * zh.x;
	irradiance.b = dot(sh[2].xyz * zh.y, N) + sh[2].w * zh.x;
	
	// 4 of the quadratic (L2) polynomials
	float4 vB = N.xyzz * N.yzzx;
	irradiance.r += dot(sh[3] * zh.z, vB) + sh[3].z / 3.0 * (zh.x - zh.z);
	irradiance.g += dot(sh[4] * zh.z, vB) + sh[4].z / 3.0 * (zh.x - zh.z);
	irradiance.b += dot(sh[5] * zh.z, vB) + sh[5].z / 3.0 * (zh.x - zh.z);

	// Final (5th) quadratic (L2) polynomial
	float vC = N.x * N.x - N.y * N.y;
	irradiance += sh[6].rgb * zh.z * vC;
	
	// Max(0) required since some zonal harmonics+ringing artifacts can cause negative values
	return max(0.0, irradiance);
}
