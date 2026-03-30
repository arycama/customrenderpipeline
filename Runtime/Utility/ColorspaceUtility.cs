public static class ColorspaceUtility
{
    /// <summary>
    /// An analytical model of chromaticity of the standard illuminant, by Judd et al.
    /// http://en.wikipedia.org/wiki/Standard_illuminant#Illuminant_series_D
    /// Slightly modifed to adjust it with the D65 white point (x=0.31271, y=0.32902).
    /// </summary>
    /// <param name="x">The input value representing the chromaticity measure.</param>
    /// <returns>Returns the calculated value of the standard illuminant's Y-coordinate based on the input x.</returns>
    public static float StandardIlluminantY(float x) => 2.87f * x - 3f * x * x - 0.27509507f;

    public static Float3 Rec709ToRec2020(Float3 x) => new
    (
        x.x * 0.627402f + x.y * 0.329292f + x.z * 0.043306f,
        x.x * 0.069095f + x.y * 0.919544f + x.z * 0.011360f,
        x.x * 0.016394f + x.y * 0.088013f + x.z * 0.895593f
    );

    public static Float3 XyzToRec2020(Float3 x) => new
    (
        x.x * 1.716651f + x.y * -0.355671f + x.z * -0.253366f,
        x.x * -0.666684f + x.y * 1.616481f + x.z * 0.015769f,
        x.x * 0.017640f + x.y * -0.042771f + x.z * 0.942103f
    );

    public static Float3 XyzToRec709(Float3 x) => new
    (
        x.x * 3.240970f + x.y * -1.537383f + x.z * -0.498611f,
        x.x * -0.969244f + x.y * 1.875968f + x.z * 0.041555f,
        x.x * 0.055630f + x.y * -0.203977f + x.z * 1.056972f
    );

    /// <summary>
    /// CIE xy chromaticity to CAT02 LMS.
    /// http://en.wikipedia.org/wiki/LMS_color_space#CAT02
    /// </summary>
    /// <param name="x">The x value in the CIE xy chromaticity.</param>
    /// <param name="y">The y value in the CIE xy chromaticity.</param>
    /// <returns>Vector3 representing the conversion from CIE xy chromaticity to CAT02 LMS color space.</returns>
    public static Float3 CIExyToLMS(float x, float y)
    {
        float Y = 1f;
        float X = Y * x / y;
        float Z = Y * (1f - x - y) / y;

        float L = 0.7328f * X + 0.4296f * Y - 0.1624f * Z;
        float M = -0.7036f * X + 1.6975f * Y + 0.0061f * Z;
        float S = 0.0030f * X + 0.0136f * Y + 0.9834f * Z;

        return new Float3(L, M, S);
    }

    /// <summary>
    /// Converts white balancing parameter to LMS coefficients.
    /// </summary>
    /// <param name="temperature">A temperature offset, in range [-100;100].</param>
    /// <param name="tint">A tint offset, in range [-100;100].</param>
    /// <returns>LMS coefficients.</returns>
    public static Float3 ColorBalanceToLMSCoeffs(float temperature, float tint)
    {
        // Range ~[-1.5;1.5] works best
        float t1 = temperature / 65f;
        float t2 = tint / 65f;

        // Get the CIE xy chromaticity of the reference white point.
        // Note: 0.31271 = x value on the D65 white point
        float x = 0.31271f - t1 * (t1 < 0f ? 0.1f : 0.05f);
        float y = StandardIlluminantY(x) + t2 * 0.05f;

        // Calculate the coefficients in the LMS space.
        var w1 = new Float3(0.949237f, 1.03542f, 1.08728f); // D65 white point
        var w2 = CIExyToLMS(x, y);
        return new Float3(w1.x / w2.x, w1.y / w2.y, w1.z / w2.z);
    }
}
