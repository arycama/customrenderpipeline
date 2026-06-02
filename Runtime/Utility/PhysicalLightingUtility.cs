using static Unmath.Math;

public static class PhysicalLightingUtility
{
    public static float CandelasToNits(float luminousIntensity, float area)
    {
        return luminousIntensity / area;
    }

    public static float CandelasToNitsDisc(float luminousIntensity, float radius)
    {
        return CandelasToNits(luminousIntensity, Pi * Sq(radius));
    }
}
