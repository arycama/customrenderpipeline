#pragma once

cbuffer ExposureBuffer
{
	float Exposure;
	float RcpExposure;
	float PreviousToCurrentExposure;
	float PreviousExposureCompensation;
};

float PaperWhite;

// This allows an emissive color to retain the same relative brightness regardless of lighting environment
float3 ApplyEmissiveExposureWeight(float3 emissive, float weight)
{
	return lerp(emissive, emissive * Exposure, weight);
}