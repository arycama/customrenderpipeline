using System;
using static Unmath.Math;

public static class PhysicalLightUtility
{
    // To Intensity (Luminance per solid angle)
    public static float PowerToIntensity(float luminousPower, float solidAngle = FourPi) => luminousPower / solidAngle;

    public static float Ev100ToIntensity(float ev100) => PhysicalCameraUtility.EV100ToLuminance(ev100);

    public static float FluxToIntensity(float lux, float distance) => lux * Sq(distance);

    public static float LuminanceToIntensity(float luminance, float projectedArea) => luminance * projectedArea;

    public static float LuminanceToIntensityDisc(float luminance, float radius) => LuminanceToIntensity(luminance, Pi * Sq(radius));

    // Luminous Power (Lumens)
    public static float IntensityToPower(float luminousIntensity, float solidAngle = FourPi) => luminousIntensity * solidAngle;

    // Luminous flux (illuminance, or lux)
    public static float IntensityToLuminance(float luminousIntensity, float projectedArea) => luminousIntensity / projectedArea;

    public static float IntensityToLuminanceDisc(float luminousIntensity, float radius) => IntensityToLuminance(luminousIntensity, Pi * Sq(radius));

    // Luminance (Luminous intensity per m2)
    public static float IntensityToEv100(float luminance) => PhysicalCameraUtility.LuminanceToEV100(luminance);

    public static float IntensityToFlux(float intensity, float distance) => intensity * Rcp(Sq(distance));
}
