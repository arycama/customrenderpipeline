using System;
using static Unmath.Math;

public static class PhysicalLightUtility
{
    public static float LuminousPowerToIntensity(float luminousPower, float solidAngle = FourPi) => luminousPower / solidAngle;

    public static float Ev100ToCandela(float ev100) => PhysicalCameraUtility.EV100ToLuminance(ev100);

    public static float CandelasToNits(float luminousIntensity, float projectedArea) => luminousIntensity / projectedArea;

    public static float CandelasToNitsDisc(float luminousIntensity, float radius) => CandelasToNits(luminousIntensity, Pi * Sq(radius));

    public static float NitsToCandelas(float luminance, float projectedArea) => luminance * projectedArea;

    public static float NitsToCandelasDisc(float luminance, float radius) => NitsToCandelas(luminance, Pi * Sq(radius));

    public static float LuminousIntensityToPower(float luminousIntensity, float solidAngle = FourPi) => luminousIntensity * solidAngle;

    public static float CandelaToEv100(float luminance) => PhysicalCameraUtility.LuminanceToEV100(luminance);
}
