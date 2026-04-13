using UnityEngine;
using static Math;

public static class PhysicalCameraUtility
{
    const float Sensitivity = 100.0f; // K
    const float LensAttenuation = 0.65f; // q
    const float LensImperfectionExposureScale = 78.0f / (Sensitivity * LensAttenuation);
    const float ReflectedLightMeterConstant = 12.5f;

    // EV100
    public static float EV100ToLuminance(float ev)
    {
        return Exp2(ev) * (ReflectedLightMeterConstant * Rcp(Sensitivity));
    }

    public static float EV100ToExposure(float ev100)
    {
        return Rcp(LensImperfectionExposureScale) * Exp2(-ev100);
    }

    // Luminance
    public static float LuminanceToEV100(float luminance)
    {
        return Log2(luminance * Sensitivity / ReflectedLightMeterConstant);
    }

    public static float LuminanceToExposure(float luminance)
    {
        var ev100 = LuminanceToEV100(luminance);
        return EV100ToExposure(ev100);
    }

    // Exposure
    public static float ExposureToEV100(float exposure)
    {
        return -Log2(LensImperfectionExposureScale * exposure);
    }

    public static float ExposureToLuminance(float exposure)
    {
        var ev100 = ExposureToEV100(exposure);
        return EV100ToLuminance(ev100);
    }

    // Other
    public static float ComputeISO(float aperture, float shutterSpeed, float ev100)
    {
        return Sq(aperture) * Sensitivity / (shutterSpeed * Exp2(ev100));
    }

    public static float ComputeEV100(float aperture, float shutterSpeed, float ISO)
    {
        return Log2(Sq(aperture) * Sensitivity / (shutterSpeed * ISO));
    }

    // Angular radius of Airy disc in radians
    public static float AiryDiscRadius(float wavelength, float apertureDiameter, float focalLength)
    {
        return 1.22f * wavelength / apertureDiameter;
    }

    public static float FocalLength(float sensorSize, float tanHalfFov)
    {
        return 0.5f * sensorSize / tanHalfFov;
    }

    public static float ApertureRadius(float focalLength, float aperture)
    {
        return 0.5f * focalLength / aperture;
    }

    public static float CalculateAiryDiscPixels(float airyDiscRadius, float sensorSize, float focalLength, float resolution)
    {
        var physicalSize = 2.0f * focalLength * Tan(airyDiscRadius);
        return physicalSize / sensorSize * resolution;
    }
}
