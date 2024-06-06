#ifndef EXPOSURE_INCLUDED
#define EXPOSURE_INCLUDED

cbuffer Exposure
{
	float _Exposure;
	float _RcpExposure;
	float _PreviousToCurrentExposure;
	float _CurrentToPreviousExposure;
};

// This allows an emissive color to retain the same relative brightness regardless of lighting environment
float3 ApplyEmissiveExposureWeight(float3 emissive, float weight)
{
	return lerp(emissive, emissive * _Exposure, weight);
}

#endif