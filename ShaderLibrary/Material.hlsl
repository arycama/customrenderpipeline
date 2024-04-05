#ifndef MATERIAL_INCLUDED
#define MATERIAL_INCLUDED

#include "Math.hlsl"

float PerceptualSmoothnessToRoughness(float perceptualSmoothness)
{
    return (1.0 - perceptualSmoothness) * (1.0 - perceptualSmoothness);
}

float RoughnessToPerceptualRoughness(float roughness)
{
	return sqrt(roughness);
}

float RoughnessToPerceptualSmoothness(float roughness)
{
    return 1.0 - sqrt(roughness);
}

// Move into material? 
// Return modified perceptualSmoothness based on provided variance (get from GeometricNormalVariance + TextureNormalVariance)
float NormalFiltering(float perceptualSmoothness, float variance, float threshold)
{
    float roughness = PerceptualSmoothnessToRoughness(perceptualSmoothness);
    // Ref: Geometry into Shading - http://graphics.pixar.com/library/BumpRoughness/paper.pdf - equation (3)
    float squaredRoughness = saturate(roughness * roughness + min(2.0 * variance, threshold * threshold)); // threshold can be really low, square the value for easier control

    return RoughnessToPerceptualSmoothness(sqrt(squaredRoughness));
}

float ProjectedSpaceNormalFiltering(float perceptualSmoothness, float variance, float threshold)
{
    float roughness = PerceptualSmoothnessToRoughness(perceptualSmoothness);
    // Ref: Stable Geometric Specular Antialiasing with Projected-Space NDF Filtering - https://yusuketokuyoshi.com/papers/2021/Tokuyoshi2021SAA.pdf
    float squaredRoughness = roughness * roughness;
    float projRoughness2 = squaredRoughness / (1.0 - squaredRoughness);
    float filteredProjRoughness2 = saturate(projRoughness2 + min(2.0 * variance, threshold * threshold));
    squaredRoughness = filteredProjRoughness2 / (filteredProjRoughness2 + 1.0f);

    return RoughnessToPerceptualSmoothness(sqrt(squaredRoughness));
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

// Return modified perceptualSmoothness
float GeometricNormalFiltering(float perceptualSmoothness, float3 geometricNormalWS, float screenSpaceVariance, float threshold)
{
    float variance = GeometricNormalVariance(geometricNormalWS, screenSpaceVariance);
    return NormalFiltering(perceptualSmoothness, variance, threshold);
}

float ProjectedSpaceGeometricNormalFiltering(float perceptualSmoothness, float3 geometricNormalWS, float screenSpaceVariance, float threshold)
{
    float variance = GeometricNormalVariance(geometricNormalWS, screenSpaceVariance);
    return ProjectedSpaceNormalFiltering(perceptualSmoothness, variance, threshold);
}

//float LengthToRoughness(float len)
//{
//	len = saturate(Remap(len, 2.0 / 3.0, 1.0));
//	float2 uv = Remap01ToHalfTexelCoord(float2(len, 0.5), float2(256.0, 1));
//	return _LengthToRoughness.SampleLevel(_LinearClampSampler, uv, 0.0);
//}

//float LengthToPerceptualRoughness(float len)
//{
//	return RoughnessToPerceptualRoughness(LengthToRoughness(len));
//}

//float LengthToSmoothness(float len)
//{
//	return RoughnessToPerceptualSmoothness(LengthToRoughness(len));
//}

//float RoughnessToNormalLength(float roughness)
//{
//	if (roughness < 1e-3)
//		return 1.0;
//	if (roughness >= 1.0)
//		return 2.0 / 3.0;

//	float a = sqrt(saturate(1.0 - Sq(roughness)));
//	return (a - (1.0 - a * a) * atanh(a)) / (a * a * a);
//}

//float PerceptualRoughnessToNormalLength(float perceptualRoughness)
//{
//	return RoughnessToNormalLength(PerceptualRoughnessToRoughness(perceptualRoughness));
//}

#endif