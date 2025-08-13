#pragma once

#include "Common.hlsl"
#include "Math.hlsl"

const static float Sensitivity = 100.0; // K
const static float LensAttenuation = 0.65; // q
const static float LensImperfectionExposureScale = 78.0 / (Sensitivity * LensAttenuation);
const static float ReflectedLightMeterConstant = 12.5;

float ExposureToEV100(float exposure)
{
	return -log2(LensImperfectionExposureScale * exposure);
}

float ComputeISO(float aperture, float shutterSpeed, float ev100)
{
	return Sq(aperture) * Sensitivity / (shutterSpeed * exp2(ev100));
}

float ComputeEV100(float aperture, float shutterSpeed, float ISO)
{
	return log2(Sq(aperture) * Sensitivity / (shutterSpeed * ISO));
}

float LuminanceToEV100(float luminance)
{
	return log2(luminance * Sensitivity / ReflectedLightMeterConstant);
}

float EV100ToLuminance(float ev)
{
	return exp2(ev) * (ReflectedLightMeterConstant * rcp(Sensitivity));
}

float EV100ToExposure(float ev100)
{
	return rcp(LensImperfectionExposureScale) * exp2(-ev100);
}
