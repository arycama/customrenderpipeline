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

// Move into material? 
// Return modified smoothness based on provided variance (get from GeometricNormalVariance + TextureNormalVariance)
float NormalFiltering(float perceptualRoughness, float variance, float threshold)
{
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    // Ref: Geometry into Shading - http://graphics.pixar.com/library/BumpRoughness/paper.pdf - equation (3)
    float squaredRoughness = saturate(roughness * roughness + min(2.0 * variance, threshold * threshold)); // threshold can be really low, square the value for easier control

    return RoughnessToPerceptualRoughness(sqrt(squaredRoughness));
}

float ProjectedSpaceNormalFiltering(float perceptualRoughness, float variance, float threshold)
{
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
    // Ref: Stable Geometric Specular Antialiasing with Projected-Space NDF Filtering - https://yusuketokuyoshi.com/papers/2021/Tokuyoshi2021SAA.pdf
    float squaredRoughness = roughness * roughness;
    float projRoughness2 = squaredRoughness / (1.0 - squaredRoughness);
    float filteredProjRoughness2 = saturate(projRoughness2 + min(2.0 * variance, threshold * threshold));
    squaredRoughness = filteredProjRoughness2 / (filteredProjRoughness2 + 1.0f);

    return RoughnessToPerceptualRoughness(sqrt(squaredRoughness));
}

// Reference: Error Reduction and Simplification for Shading Anti-Aliasing
// Specular antialiasing for geometry-induced normal (and NDF) variations: Tokuyoshi / Kaplanyan et al.'s method.
// This is the deferred approximation, which works reasonably well so we keep it for forward too for now.
// screenSpaceVariance should be at most 0.5^2 = 0.25, as that corresponds to considering
// a gaussian pixel reconstruction kernel with a standard deviation of 0.5 of a pixel, thus 2 sigma covering the whole pixel.
float GeometricNormalVariance(float3 geometricNormalWS, float screenSpaceVariance)
{
    float3 deltaU = ddx(geometricNormalWS);
    float3 deltaV = ddy(geometricNormalWS);

    return screenSpaceVariance * (dot(deltaU, deltaU) + dot(deltaV, deltaV));
}

// Return modified smoothness
float GeometricNormalFiltering(float perceptualRoughness, float3 geometricNormalWS, float screenSpaceVariance, float threshold)
{
    float variance = GeometricNormalVariance(geometricNormalWS, screenSpaceVariance);
	return NormalFiltering(perceptualRoughness, variance, threshold);
}

float ProjectedSpaceGeometricNormalFiltering(float perceptualRoughness, float3 geometricNormalWS, float screenSpaceVariance, float threshold)
{
    float variance = GeometricNormalVariance(geometricNormalWS, screenSpaceVariance);
	return ProjectedSpaceNormalFiltering(perceptualRoughness, variance, threshold);
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