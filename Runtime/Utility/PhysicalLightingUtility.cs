public static class PhysicalLightingUtility
{
    public static float CandelasToNits(float luminousIntensity, float area)
    {
        return luminousIntensity / area;
    }

    public static float CandelasToNitsDisc(float luminousIntensity, float radius)
    {
        return CandelasToNits(luminousIntensity, Math.Pi * Math.Sq(radius));
    }
}
