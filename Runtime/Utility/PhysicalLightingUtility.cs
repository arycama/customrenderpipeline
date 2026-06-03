using static Unmath.Math;

public static class PhysicalLightingUtility
{
    public static float CandelasToNits(float luminousIntensity, float projectedArea)
    {
        return luminousIntensity / projectedArea;
    }

    public static float CandelasToNitsDisc(float luminousIntensity, float radius)
    {
        return CandelasToNits(luminousIntensity, Pi * Sq(radius));
    }

    public static float NitsToCandelas(float luminance, float projectedArea)
    {
        return luminance * projectedArea;
    }

    public static float NitsToCandelasDisc(float luminance, float radius)
    {
        return NitsToCandelas(luminance, Pi * Sq(radius));
    }
}
