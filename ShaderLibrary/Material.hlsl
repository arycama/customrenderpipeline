#ifndef MATERIAL_INCLUDED
#define MATERIAL_INCLUDED

#include "Common.hlsl"
#include "Math.hlsl"
#include "Samplers.hlsl"

Texture2D<float> _LengthToRoughness;

float SmoothnessToPerceptualRoughness(float smoothness)
{
	return 1.0 - smoothness;
}

float SmoothnessToRoughness(float smoothness)
{
	// Sq(1-smoothness) rewritten as 2 mads, vs sub sub mul
	return -2.0 * smoothness + (smoothness * smoothness + 1.0);
}

float RoughnessToPerceptualRoughness(float roughness)
{
	return sqrt(roughness);
}

float RoughnessToSmoothness(float roughness)
{
    return 1.0 - sqrt(roughness);
}

float SpecularAntiAliasing(float perceptualRoughness, float3 worldNormal, float variance, float threshold)
{
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	float roughness2 = Sq(roughness);

	float SIGMA2 = variance; //2.0 * 0.15915494;
	float KAPPA = threshold;//0.18;
	float3 dndu = ddx(worldNormal);
	float3 dndv = ddy(worldNormal);
	float kernelRoughness2 = SIGMA2 * (dot(dndu, dndu) + dot(dndv, dndv));
	float clampedKernelRoughness2 = min(kernelRoughness2, KAPPA);
	float filteredRoughness2 = saturate(roughness2 + clampedKernelRoughness2);
	float filteredRoughness = sqrt(filteredRoughness2);
	return RoughnessToPerceptualRoughness(filteredRoughness);
}

float LengthToRoughness(float len)
{
	len = saturate(Remap(len, 2.0 / 3.0, 1.0));
	float2 uv = Remap01ToHalfTexel(float2(len, 0.5), float2(256.0, 1));
	return _LengthToRoughness.SampleLevel(_LinearClampSampler, uv, 0.0);
}

float LengthToPerceptualRoughness(float len)
{
	return RoughnessToPerceptualRoughness(LengthToRoughness(len));
}

float LengthToSmoothness(float len)
{
	return RoughnessToSmoothness(LengthToRoughness(len));
}

float RoughnessToNormalLength(float roughness)
{
	if(roughness < 1e-3)
		return 1.0;
	if(roughness >= 1.0)
		return 2.0 / 3.0;

	float a = sqrt(saturate(1.0 - Sq(roughness)));
	return (a - (1.0 - a * a) * atanh(a)) / (a * a * a);
}

float PerceptualRoughnessToNormalLength(float perceptualRoughness)
{
	return RoughnessToNormalLength(PerceptualRoughnessToRoughness(perceptualRoughness));
}

float SmoothnessToNormalLength(float smoothness)
{
	return RoughnessToNormalLength(SmoothnessToRoughness(smoothness));
}

#endif