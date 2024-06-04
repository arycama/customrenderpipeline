using System;
using UnityEngine;
using static System.MathF;
using static Arycama.CustomRenderPipeline.MathUtils;

namespace Arycama.CustomRenderPipeline
{
    public class Aces
    {
        public struct SegmentedSplineParams_c9
        {
            public Vector4[] coefs;// = new Vector4[10];
            public Vector2 minPoint; // {luminance, luminance} linear extension below this
            public Vector2 midPoint; // {luminance, luminance} 
            public Vector2 maxPoint; // {luminance, luminance} linear extension above this
            public Vector2 slope;
            public Vector2 limits; // limits in ODT curve prior to RRT adjustment

            public SegmentedSplineParams_c9(Vector4[] coefs, Vector2 minPoint, Vector2 midPoint, Vector2 maxPoint, Vector2 slope, Vector2 limits)
            {
                this.coefs = coefs ?? throw new ArgumentNullException(nameof(coefs));
                this.minPoint = minPoint;
                this.midPoint = midPoint;
                this.maxPoint = maxPoint;
                this.slope = slope;
                this.limits = limits;
            }
        };

        public struct ACESparams
        {
            public SegmentedSplineParams_c9 C;
            public Matrix3x3 XYZ_2_DISPLAY_PRI_MAT;
            public Matrix3x3 DISPLAY_PRI_MAT_2_XYZ;
            public Vector2 CinemaLimits;
            public int OutputMode;
            public float surroundGamma;
            public bool desaturate;
            public bool surroundAdjust;
            public bool applyCAT;
            public bool tonemapLuminance;
            public float saturationLevel;

            public ACESparams(SegmentedSplineParams_c9 c, Matrix3x3 xYZ_2_DISPLAY_PRI_MAT, Matrix3x3 dISPLAY_PRI_MAT_2_XYZ, Vector2 cinemaLimits, int outputMode, float surroundGamma, bool desaturate, bool surroundAdjust, bool applyCAT, bool tonemapLuminance, float saturationLevel)
            {
                C = c;
                XYZ_2_DISPLAY_PRI_MAT = xYZ_2_DISPLAY_PRI_MAT;
                DISPLAY_PRI_MAT_2_XYZ = dISPLAY_PRI_MAT_2_XYZ;
                CinemaLimits = cinemaLimits;
                OutputMode = outputMode;
                this.surroundGamma = surroundGamma;
                this.desaturate = desaturate;
                this.surroundAdjust = surroundAdjust;
                this.applyCAT = applyCAT;
                this.tonemapLuminance = tonemapLuminance;
                this.saturationLevel = saturationLevel;
            }
        };

        // Struct with RRT spline parameters
        struct SegmentedSplineParams_c5
        {
            public float[] coefsLow;    // coefs for B-spline between minPoint and midPoint (units of log luminance)
            public float[] coefsHigh;   // coefs for B-spline between midPoint and maxPoint (units of log luminance)
            public Vector2 minPoint; // {luminance, luminance} linear extension below this
            public Vector2 midPoint; // {luminance, luminance} 
            public Vector2 maxPoint; // {luminance, luminance} linear extension above this
            public float slopeLow;       // log-log slope of low linear extension
            public float slopeHigh;      // log-log slope of high linear extension

            public SegmentedSplineParams_c5(float[] coefsLow, float[] coefsHigh, Vector2 minPoint, Vector2 midPoint, Vector2 maxPoint, float slopeLow, float slopeHigh)
            {
                this.coefsLow = coefsLow ?? throw new ArgumentNullException(nameof(coefsLow));
                this.coefsHigh = coefsHigh ?? throw new ArgumentNullException(nameof(coefsHigh));
                this.minPoint = minPoint;
                this.midPoint = midPoint;
                this.maxPoint = maxPoint;
                this.slopeLow = slopeLow;
                this.slopeHigh = slopeHigh;
            }
        };

        // Struct with ODT spline parameters
        struct SegmentedSplineParams_c9_internal
        {
            public float[] coefsLow;    // coefs for B-spline between minPoint and midPoint (units of log luminance)
            public float[] coefsHigh;   // coefs for B-spline between midPoint and maxPoint (units of log luminance)
            public Vector2 minPoint; // {luminance, luminance} linear extension below this
            public Vector2 midPoint; // {luminance, luminance} 
            public Vector2 maxPoint; // {luminance, luminance} linear extension above this
            public Vector2 slope;
            public Vector2 limits; // Min and Max prior to RRT

            public SegmentedSplineParams_c9_internal(float[] coefsLow, float[] coefsHigh, Vector2 minPoint, Vector2 midPoint, Vector2 maxPoint, Vector2 slope, Vector2 limits)
            {
                this.coefsLow = coefsLow ?? throw new ArgumentNullException(nameof(coefsLow));
                this.coefsHigh = coefsHigh ?? throw new ArgumentNullException(nameof(coefsHigh));
                this.minPoint = minPoint;
                this.midPoint = midPoint;
                this.maxPoint = maxPoint;
                this.slope = slope;
                this.limits = limits;
            }
        };

        //  Spline function used by RRT
        static float segmented_spline_c5_fwd(float x)
        {
            // RRT_PARAMS
            SegmentedSplineParams_c5 C = new(
                new float[] { -4.0000000000f, -4.0000000000f, -3.1573765773f, -0.4852499958f, 1.8477324706f, 1.8477324706f },
                new float[] { -0.7185482425f, 2.0810307172f, 3.6681241237f, 4.0000000000f, 4.0000000000f, 4.0000000000f },
                new Vector2(0.18f * Exp2(-15.0f), 0.0001f),
                new Vector2(0.18f, 4.8f),
                new Vector2(0.18f * Exp2(18.0f), 10000.0f),
                0.0f,
                0.0f);

            // Textbook monomial to basis-function conversion matrix.
            Matrix3x3 M = new(0.5f, -1.0f, 0.5f, -1.0f, 1.0f, 0.5f, 0.5f, 0.0f, 0.0f);

            const int N_KNOTS_LOW = 4;
            const int N_KNOTS_HIGH = 4;

            // Check for negatives or zero before taking the log. If negative or zero,
            // set to ACESMIN.1
            float xCheck = x <= 0 ? Exp2(-14.0f) : x;

            float logx = Log10(xCheck);
            float logy;

            if (logx <= Log10(C.minPoint.x))
            {
                logy = logx * C.slopeLow + (Log10(C.minPoint.y) - C.slopeLow * Log10(C.minPoint.x));
            }
            else if ((logx > Log10(C.minPoint.x)) && (logx < Log10(C.midPoint.x)))
            {
                float knot_coord = (N_KNOTS_LOW - 1) * (logx - Log10(C.minPoint.x)) / (Log10(C.midPoint.x) - Log10(C.minPoint.x));
                int j = (int)(knot_coord);
                float t = knot_coord - j;

                Vector3 cf = new(C.coefsLow[j], C.coefsLow[j + 1], C.coefsLow[j + 2]);

                Vector3 monomials = new(t * t, t, 1);
                //logy = dot(monomials, Mul(cf, M));
                //logy = Vector3.DotProduct(monomials, M.TransformVector(cf));
                Vector3 basis = cf.x * M.GetRow(0) + cf.y * M.GetRow(1) + cf.z * M.GetRow(2);
                logy = Vector3.Dot(monomials, basis);
            }
            else if ((logx >= Log10(C.midPoint.x)) && (logx < Log10(C.maxPoint.x)))
            {
                float knot_coord = (N_KNOTS_HIGH - 1) * (logx - Log10(C.midPoint.x)) / (Log10(C.maxPoint.x) - Log10(C.midPoint.x));
                int j = (int)(knot_coord);
                float t = knot_coord - j;

                Vector3 cf = new(C.coefsHigh[j], C.coefsHigh[j + 1], C.coefsHigh[j + 2]);

                Vector3 monomials = new(t * t, t, 1);
                //logy = dot(monomials, Mul(cf, M));
                //logy = Vector3.DotProduct(monomials, M.TransformVector(cf));
                Vector3 basis = cf.x * M.GetRow(0) + cf.y * M.GetRow(1) + cf.z * M.GetRow(2);
                logy = Vector3.Dot(monomials, basis);
            }
            else
            { //if ( logIn >= Log10(C.maxPoint.x) ) { 
                logy = logx * C.slopeHigh + (Log10(C.maxPoint.y) - C.slopeHigh * Log10(C.maxPoint.x));
            }

            return Exp10(logy);
        }

        static void mul3(out Vector3 res, Vector3 a, Matrix3x3 M)
        {
            res = a.x * M.GetRow(0) + a.y * M.GetRow(1) + a.z * M.GetRow(2);
        }

        // Using a reference ODT spline, adjust middle gray and Max levels
        static SegmentedSplineParams_c9_internal AdaptSpline(SegmentedSplineParams_c9_internal C, float newMin, float newMax, float outMax, float outMidScale)
        {
            // Monomial and inverse monomial matrices
            var M = new Matrix3x3(0.5f, -1.0f, 0.5f, -1.0f, 1.0f, 0.5f, 0.5f, 0.0f, 0.0f);
            var iM = new Matrix3x3(0.0f, 0.0f, 2.0f, -0.5f, 0.5f, 1.5f, 1.0f, 1.0f, 1.0f);

            const int N_KNOTS_LOW = 8;
            const int N_KNOTS_HIGH = 8;

            SegmentedSplineParams_c9_internal C2 = C;

            // Set the new Max input and output levels
            C2.maxPoint.x = segmented_spline_c5_fwd(newMax);
            C2.maxPoint.y = outMax;

            C2.limits.y = newMax;

            // Set new minimum levels
            C2.minPoint.x = segmented_spline_c5_fwd(newMin);

            C2.limits.x = newMin;

            // scale the middle gray output level
            C2.midPoint.y *= outMidScale;

            // compute and apply scale used to bring bottom segment of the transform to the level desired for middle gray
            float scale = (Log10(C.midPoint[1]) - Log10(C.minPoint[1])) / (Log10(C2.midPoint[1]) - Log10(C2.minPoint[1]));

            for (int j = 0; j < N_KNOTS_LOW + 2; j++)
            {
                C2.coefsLow[j] = (C2.coefsLow[j] - Log10(C2.minPoint[1])) / scale + Log10(C2.minPoint[1]);
            }

            // compute and apply scale to top segment of the transform to the level matching the new Max and middle gray
            scale = (Log10(C.maxPoint[1]) - Log10(C.midPoint[1])) / (Log10(C2.maxPoint[1]) - Log10(C2.midPoint[1]));

            var target = new float[10]; // saves the "target" values, as we need to match/relax the spline to properly join the low segment

            for (int j = 0; j < N_KNOTS_HIGH + 2; j++)
            {
                C2.coefsHigh[j] = (C2.coefsHigh[j] - Log10(C.midPoint[1])) / scale + Log10(C2.midPoint[1]);
                target[j] = C2.coefsHigh[j];
            }

            // Adjust the high spline to properly meet the low spline, then relax the high spline

            // Coefficients for the last segment of the low range
            Vector3 cfl = new(C2.coefsLow[7], C2.coefsLow[8], C2.coefsLow[9]);

            // Coeffiecients for the first segment of the high range
            Vector3 cfh = new(C2.coefsHigh[0], C2.coefsHigh[1], C2.coefsHigh[2]);

            Vector3 cflt, cfht;

            // transform the coefficients by the monomial matrix
            mul3(out cflt, cfl, M);
            mul3(out cfht, cfh, M);

            // low and high curves cover different ranges, so compute scaling factor needed to match slopes at the join point
            float scaleLow = 1.0f / (Log10(C2.midPoint[0]) - Log10(C2.minPoint[0]));
            float scaleHigh = 1.0f / (Log10(C2.maxPoint[0]) - Log10(C2.midPoint[0]));

            // compute the targeted exit point for the segment 
            float outRef = cfht[0] * 2.0f + cfht[1]; //slope at t == 1

            // match slopes and intersection
            cfht[2] = cflt[2];
            cfht[1] = (scaleLow * cflt[1]) / scaleHigh;

            // compute the exit point the segment has after the adjustment
            float o = cfht[0] * 2.0f + cfht[1]; //result at t == 1

            // ease spline toward target and adjust
            float outTarget = (outRef * 7.0f + o * 1.0f) / 8.0f;
            cfht[0] = (outTarget - cfht[1]) / 2.0f;

            // back-transform  the adjustments and save them
            Vector3 acfh;

            mul3(out acfh, cfht, iM);

            C2.coefsHigh[0] = acfh[0];
            C2.coefsHigh[1] = acfh[1];
            C2.coefsHigh[2] = acfh[2];

            // now correct the rest of the spline
            for (int j = 1; j < N_KNOTS_HIGH; j++)
            {
                //  Original rescaled spline values for the segment (ideal "target")
                Vector3 cfoh = new(target[j], target[j + 1], target[j + 2]);

                //  New spline values for the segment based on alterations to prior ranges
                Vector3 cfh1 = new(C2.coefsHigh[j], C2.coefsHigh[j + 1], C2.coefsHigh[j + 2]);

                Vector3 cfht1, cfoht1;

                mul3(out cfht1, cfh1, M);
                mul3(out cfoht1, cfoh, M);

                //Compute exit slope for segments
                float o1 = cfht1[0] * 2.0f + cfht1[1]; //slope at t == 1
                float outRef1 = cfoht1[0] * 2.0f + cfoht1[1]; //slope at t == 1

                //Ease spline toward targetted slope
                float outTarget1 = (outRef1 * (7.0f - j) + o1 * (1.0f + j)) / 8.0f;
                cfht1[0] = (outTarget1 - cfht1[1]) / 2.0f;

                // Back transform and save
                Vector3 acfh1;

                mul3(out acfh1, cfht1, iM);

                C2.coefsHigh[j] = acfh1[0];
                C2.coefsHigh[j + 1] = acfh1[1];
                C2.coefsHigh[j + 2] = acfh1[2];
            }

            return C2;
        }

        // ODT_48nits
        static SegmentedSplineParams_c9_internal ODT_48nits = new SegmentedSplineParams_c9_internal
        (
            new[] { -1.6989700043f, -1.6989700043f, -1.4779000000f, -1.2291000000f, -0.8648000000f, -0.4480000000f, 0.0051800000f, 0.4511080334f, 0.9113744414f, 0.9113744414f },
            new[] { 0.5154386965f, 0.8470437783f, 1.1358000000f, 1.3802000000f, 1.5197000000f, 1.5985000000f, 1.6467000000f, 1.6746091357f, 1.6878733390f, 1.6878733390f },
            new Vector2(segmented_spline_c5_fwd(0.18f * Exp2(-6.5f)), 0.02f),
            new Vector2(segmented_spline_c5_fwd(0.18f), 4.8f),
            new Vector2(segmented_spline_c5_fwd(0.18f * Exp2(6.5f)), 48.0f),
            new Vector2(0.0f, 0.04f),
            new Vector2(0.18f * Exp2(-6.5f), 0.18f * Exp2(6.5f))
        );

        // ODT_1000nits
        static SegmentedSplineParams_c9_internal ODT_1000nits = new SegmentedSplineParams_c9_internal
        (
            new[] { -2.3010299957f, -2.3010299957f, -1.9312000000f, -1.5205000000f, -1.0578000000f, -0.4668000000f, 0.1193800000f, 0.7088134201f, 1.2911865799f, 1.2911865799f },
            new[] { 0.8089132070f, 1.1910867930f, 1.5683000000f, 1.9483000000f, 2.3083000000f, 2.6384000000f, 2.8595000000f, 2.9872608805f, 3.0127391195f, 3.0127391195f },
            new Vector2(segmented_spline_c5_fwd(0.18f * Pow(2.0f, -12.0f)), 0.005f),
            new Vector2(segmented_spline_c5_fwd(0.18f), 10.0f),
            new Vector2(segmented_spline_c5_fwd(0.18f * Pow(2.0f, 10.0f)), 1000.0f),
            new Vector2(0.0f, 0.06f),
            new Vector2(0.18f * Exp2(-12.0f), 0.18f * Exp2(10.0f))
        );

        // ODT_2000nits
        static SegmentedSplineParams_c9_internal ODT_2000nits = new SegmentedSplineParams_c9_internal
        (
            new[] { -2.3010299957f, -2.3010299957f, -1.9312000000f, -1.5205000000f, -1.0578000000f, -0.4668000000f, 0.1193800000f, 0.7088134201f, 1.2911865799f, 1.2911865799f },
            new[] { 0.8019952042f, 1.1980047958f, 1.5943000000f, 1.9973000000f, 2.3783000000f, 2.7684000000f, 3.0515000000f, 3.2746293562f, 3.3274306351f, 3.3274306351f },
            new Vector2(segmented_spline_c5_fwd(0.18f * Pow(2.0f, -12.0f)), 0.005f),
            new Vector2(segmented_spline_c5_fwd(0.18f), 10.0f),
            new Vector2(segmented_spline_c5_fwd(0.18f * Pow(2.0f, 11.0f)), 2000.0f),
            new Vector2(0.0f, 0.12f),
            new Vector2(0.18f * Exp2(-12.0f), 0.18f * Exp2(11.0f))
        );

        // ODT_4000nits
        static SegmentedSplineParams_c9_internal ODT_4000nits = new SegmentedSplineParams_c9_internal
        (
            new[] { -2.3010299957f, -2.3010299957f, -1.9312000000f, -1.5205000000f, -1.0578000000f, -0.4668000000f, 0.1193800000f, 0.7088134201f, 1.2911865799f, 1.2911865799f },
            new[] { 0.7973186613f, 1.2026813387f, 1.6093000000f, 2.0108000000f, 2.4148000000f, 2.8179000000f, 3.1725000000f, 3.5344995451f, 3.6696204376f, 3.6696204376f },
            new Vector2(segmented_spline_c5_fwd(0.18f * Pow(2.0f, -12.0f)), 0.005f),
            new Vector2(segmented_spline_c5_fwd(0.18f), 10.0f),
            new Vector2(segmented_spline_c5_fwd(0.18f * Pow(2.0f, 12.0f)), 4000.0f),
            new Vector2(0.0f, 0.3f),
            new Vector2(0.18f * Exp2(-12.0f), 0.18f * Exp2(12.0f))
        );

        // Select a curve used as part of the ACES ODT. Optionally, derive a modified version of the base curve with an altered middle gray and maximum number of stops.
        public static SegmentedSplineParams_c9 GetAcesODTData(ODTCurve BaseCurve, float MinStop, float MaxStop, float MaxLevel, float MidGrayScale)
        {
            // Standard ACES ODT curves
            // convert, defaulting to 48 nits
            var Src = ODT_48nits;
            SegmentedSplineParams_c9_internal Generated;
            SegmentedSplineParams_c9 C;

            switch (BaseCurve)
            {
                case ODTCurve.ODT_LDR_Ref: Src = ODT_48nits; break;
                case ODTCurve.ODT_1000Nit_Ref: Src = ODT_1000nits; break;
                case ODTCurve.ODT_2000Nit_Ref: Src = ODT_2000nits; break;
                case ODTCurve.ODT_4000Nit_Ref: Src = ODT_4000nits; break;

                // Adjustable curves
                case ODTCurve.ODT_LDR_Adj:
                    MaxLevel = MaxLevel > 0.0f ? MaxLevel : 48.0f;
                    MaxStop = MaxStop > 0 ? MaxStop : 6.5f;
                    MinStop = MinStop < 0 ? MinStop : -6.5f;
                    Generated = AdaptSpline(ODT_48nits, 0.18f * Pow(2.0f, MinStop), 0.18f * Pow(2.0f, MaxStop), MaxLevel, MidGrayScale);
                    Src = Generated;
                    break;

                case ODTCurve.ODT_1000Nit_Adj:
                    MaxLevel = MaxLevel > 0.0f ? MaxLevel : 1000.0f;
                    MaxStop = MaxStop > 0 ? MaxStop : 10.0f;
                    MinStop = MinStop < 0 ? MinStop : -12.0f;
                    Generated = AdaptSpline(ODT_1000nits, 0.18f * Pow(2.0f, MinStop), 0.18f * Pow(2.0f, MaxStop), MaxLevel, MidGrayScale);
                    Src = Generated;
                    break;

                case ODTCurve.ODT_2000Nit_Adj:
                    MaxLevel = MaxLevel > 0.0f ? MaxLevel : 2000.0f;
                    MaxStop = MaxStop > 0 ? MaxStop : 11.0f;
                    MinStop = MinStop < 0 ? MinStop : -12.0f;
                    Generated = AdaptSpline(ODT_2000nits, 0.18f * Pow(2.0f, MinStop), 0.18f * Pow(2.0f, MaxStop), MaxLevel, MidGrayScale);
                    Src = Generated;
                    break;

                case ODTCurve.ODT_4000Nit_Adj:
                    MaxLevel = MaxLevel > 0.0f ? MaxLevel : 4000.0f;
                    MaxStop = MaxStop > 0 ? MaxStop : 12.0f;
                    MinStop = MinStop < 0 ? MinStop : -12.0f;
                    Generated = AdaptSpline(ODT_4000nits, 0.18f * Pow(2.0f, MinStop), 0.18f * Pow(2.0f, MaxStop), MaxLevel, MidGrayScale);
                    Src = Generated;
                    break;
            };

            {
                var Curve = Src;

                C.coefs = new Vector4[10];
                for (int Index = 0; Index < 10; Index++)
                {
                    C.coefs[Index] = new Vector4(Curve.coefsLow[Index], Curve.coefsHigh[Index], 0.0f, 0.0f);
                }

                C.minPoint = Curve.minPoint;
                C.midPoint = Curve.midPoint;
                C.maxPoint = Curve.maxPoint;
                C.slope = Curve.slope;
                C.limits = Curve.limits;
            }

            return C;
        }

        const float TINY = 1e-10f;
        const float M_PI = 3.1415927f;
        const float HALF_MAX = 65504.0f;

        static Matrix3x3 AP0_2_XYZ_MAT = new
        (
            0.95255238f, 0.00000000f, 0.00009368f,
            0.34396642f, 0.72816616f, -0.07213254f,
            -0.00000004f, 0.00000000f, 1.00882506f
        );

        static Matrix3x3 XYZ_2_AP0_MAT = new
        (
            1.04981101f, -0.00000000f, -0.00009748f,
            -0.49590296f, 1.37331295f, 0.09824003f,
            0.00000004f, -0.00000000f, 0.99125212f
        );

        static Matrix3x3 AP1_2_XYZ_MAT = new
        (
            0.66245413f, 0.13400421f, 0.15618768f,
            0.27222872f, 0.67408168f, 0.05368952f,
            -0.00557466f, 0.00406073f, 1.01033902f
        );

        static Matrix3x3 XYZ_2_AP1_MAT = new
        (
            1.64102352f, -0.32480335f, -0.23642471f,
            -0.66366309f, 1.61533189f, 0.01675635f,
            0.01172191f, -0.00828444f, 0.98839492f
        );

        static Matrix3x3 AP0_2_AP1_MAT = new
        (
            1.45143950f, -0.23651081f, -0.21492855f,
            -0.07655388f, 1.17623007f, -0.09967594f,
            0.00831613f, -0.00603245f, 0.99771625f
        );

        static Matrix3x3 AP1_2_AP0_MAT = new
        (
            0.69545215f, 0.14067869f, 0.16386905f,
            0.04479461f, 0.85967094f, 0.09553432f,
            -0.00552587f, 0.00402521f, 1.00150073f
        );

        // EHart - need to check this, might be a transpose issue with CTL
        static Vector3 AP1_RGB2Y = new(0.27222872f, 0.67408168f, 0.05368952f);

        static float max_f3(Vector3 In)
        {
            return Max(In.x, Max(In.y, In.z));
        }

        float min_f3(Vector3 In)
        {
            return Min(In.x, Min(In.y, In.z));
        }

        float rgb_2_saturation(Vector3 rgb)
        {
            return (Max(max_f3(rgb), TINY) - Max(min_f3(rgb), TINY)) / Max(max_f3(rgb), 1e-2f);
        }

        /* ---- Conversion Functions ---- */
        // Various transformations between color encodings and data representations
        //

        // Transformations between CIE XYZ tristimulus values and CIE x,y 
        // chromaticity coordinates
        Vector3 XYZ_2_xyY(Vector3 XYZ)
        {
            Vector3 xyY;
            float divisor = (XYZ[0] + XYZ[1] + XYZ[2]);
            if (divisor == 0.0f) divisor = 1e-10f;
            xyY.x = XYZ[0] / divisor;
            xyY.y = XYZ[1] / divisor;
            xyY.z = XYZ[1];

            return xyY;
        }

        Vector3 xyY_2_XYZ(Vector3 xyY)
        {
            Vector3 XYZ;
            XYZ.x = xyY[0] * xyY[2] / Max(xyY[1], 1e-10f);
            XYZ.y = xyY[2];
            XYZ.z = (1.0f - xyY[0] - xyY[1]) * xyY[2] / Max(xyY[1], 1e-10f);

            return XYZ;
        }


        // Transformations from RGB to other color representations
        float rgb_2_hue(Vector3 rgb)
        {
            // Returns a geometric hue angle in degrees (0-360) based on RGB values.
            // For neutral colors, hue is undefined and the function will return a quiet NaN value.
            float hue;
            if (rgb[0] == rgb[1] && rgb[1] == rgb[2])
            {
                // RGB triplets where RGB are equal have an undefined hue
                // EHart - reference code uses NaN, use 0 instead to prevent propagation of NaN
                hue = 0.0f;
            }
            else
            {
                hue = (180.0f / M_PI) * Atan2(Sqrt(3.0f) * (rgb[1] - rgb[2]), 2.0f * rgb[0] - rgb[1] - rgb[2]);
            }

            if (hue < 0.0f) hue = hue + 360.0f;

            return hue;
        }

        float rgb_2_yc(Vector3 rgb, float ycRadiusWeight = 1.75f)
        {
            // Converts RGB to a luminance proxy, here called YC
            // YC is ~ Y + K * Chroma
            // Constant YC is a cone-shaped surface in RGB space, with the tip on the 
            // neutral axis, towards white.
            // YC is normalized: RGB 1 1 1 maps to YC = 1
            //
            // ycRadiusWeight defaults to 1.75, although can be overridden in function 
            // call to rgb_2_yc
            // ycRadiusWeight = 1 -> YC for pure cyan, magenta, yellow == YC for neutral 
            // of same value
            // ycRadiusWeight = 2 -> YC for pure red, green, blue  == YC for  neutral of 
            // same value.

            float r = rgb[0];
            float g = rgb[1];
            float b = rgb[2];

            float chroma = Sqrt(b * (b - g) + g * (g - r) + r * (r - b));

            return (b + g + r + ycRadiusWeight * chroma) / 3.0f;
        }

        /* ODT utility functions */
        float Y_2_linCV(float Y, float Ymax, float Ymin)
        {
            return (Y - Ymin) / (Ymax - Ymin);
        }

        float linCV_2_Y(float linCV, float Ymax, float Ymin)
        {
            return linCV * (Ymax - Ymin) + Ymin;
        }

        // Gamma compensation factor
        const float DIM_SURROUND_GAMMA = 0.9811f;

        Vector3 darkSurround_to_dimSurround(Vector3 linearCV)
        {
            Vector3 XYZ = Mul(AP1_2_XYZ_MAT, linearCV);

            Vector3 xyY = XYZ_2_xyY(XYZ);
            xyY[2] = Max(xyY[2], 0.0f);
            xyY[2] = Pow(xyY[2], DIM_SURROUND_GAMMA);
            XYZ = xyY_2_XYZ(xyY);

            return Mul(XYZ_2_AP1_MAT, XYZ);
        }

        Vector3 dimSurround_to_darkSurround(Vector3 linearCV)
        {
            Vector3 XYZ = Mul(AP1_2_XYZ_MAT, linearCV);

            Vector3 xyY = XYZ_2_xyY(XYZ);
            xyY[2] = Max(xyY[2], 0.0f);
            xyY[2] = Pow(xyY[2], 1.0f / DIM_SURROUND_GAMMA);
            XYZ = xyY_2_XYZ(xyY);

            return Mul(XYZ_2_AP1_MAT, XYZ);
        }

        Vector3 alter_surround(Vector3 linearCV, float gamma)
        {
            Vector3 XYZ = Mul(AP1_2_XYZ_MAT, linearCV);

            Vector3 xyY = XYZ_2_xyY(XYZ);
            xyY[2] = Max(xyY[2], 0.0f);
            xyY[2] = Pow(xyY[2], gamma);
            XYZ = xyY_2_XYZ(xyY);

            return Mul(XYZ_2_AP1_MAT, XYZ);
        }

        Matrix3x3 calc_sat_adjust_matrix(float sat, Vector3 rgb2Y)
        {
            //
            // This function determines the terms for a 3x3 saturation matrix that is
            // based on the luminance of the input.
            //
            Matrix3x3 M = new();

            M[0] = (1.0f - sat) * rgb2Y[0] + sat;
            M[3] = (1.0f - sat) * rgb2Y[0];
            M[6] = (1.0f - sat) * rgb2Y[0];

            M[1] = (1.0f - sat) * rgb2Y[1];
            M[4] = (1.0f - sat) * rgb2Y[1] + sat;
            M[7] = (1.0f - sat) * rgb2Y[1];

            M[2] = (1.0f - sat) * rgb2Y[2];
            M[5] = (1.0f - sat) * rgb2Y[2];
            M[8] = (1.0f - sat) * rgb2Y[2] + sat;

            // EHart - removed transpose, as the indexing in CTL is transposed

            return M;
        }

        /* ---- Signal encode/decode functions ---- */

        float moncurve_f(float x, float gamma, float offs)
        {
            // Forward monitor curve
            float y;
            float fs = ((gamma - 1.0f) / offs) * Pow(offs * gamma / ((gamma - 1.0f) * (1.0f + offs)), gamma);
            float xb = offs / (gamma - 1.0f);
            if (x >= xb)
                y = Pow((x + offs) / (1.0f + offs), gamma);
            else
                y = x * fs;
            return y;
        }

        float moncurve_r(float y, float gamma, float offs)
        {
            // Reverse monitor curve
            float x;
            float yb = Pow(offs * gamma / ((gamma - 1.0f) * (1.0f + offs)), gamma);
            float rs = Pow((gamma - 1.0f) / offs, gamma - 1.0f) * Pow((1.0f + offs) / gamma, gamma);
            if (y >= yb)
                x = (1.0f + offs) * Pow(y, 1.0f / gamma) - offs;
            else
                x = y * rs;
            return x;
        }

        // Base functions from SMPTE ST 2084-2014

        // Constants from SMPTE ST 2084-2014
        const float pq_m1 = 0.1593017578125f; // ( 2610.0 / 4096.0 ) / 4.0;
        const float pq_m2 = 78.84375f; // ( 2523.0 / 4096.0 ) * 128.0;
        const float pq_c1 = 0.8359375f; // 3424.0 / 4096.0 or pq_c3 - pq_c2 + 1.0;
        const float pq_c2 = 18.8515625f; // ( 2413.0 / 4096.0 ) * 32.0;
        const float pq_c3 = 18.6875f; // ( 2392.0 / 4096.0 ) * 32.0;

        const float pq_C = 10000.0f;

        // Converts from the non-linear perceptually quantized space to linear cd/m^2
        // Note that this is in float, and assumes normalization from 0 - 1
        // (0 - pq_C for linear) and does not handle the integer coding in the Annex 
        // sections of SMPTE ST 2084-2014
        float pq_f(float N)
        {
            // Note that this does NOT handle any of the signal range
            // considerations from 2084 - this assumes full range (0 - 1)
            float Np = Pow(N, 1.0f / pq_m2);
            float L = Np - pq_c1;
            if (L < 0.0f)
                L = 0.0f;
            L = L / (pq_c2 - pq_c3 * Np);
            L = Pow(L, 1.0f / pq_m1);
            return L * pq_C; // returns cd/m^2
        }

        // Converts from linear cd/m^2 to the non-linear perceptually quantized space
        // Note that this is in float, and assumes normalization from 0 - 1
        // (0 - pq_C for linear) and does not handle the integer coding in the Annex 
        // sections of SMPTE ST 2084-2014
        float pq_r(float C)
        {
            // Note that this does NOT handle any of the signal range
            // considerations from 2084 - this returns full range (0 - 1)
            float L = C / pq_C;
            float Lm = Pow(L, pq_m1);
            float N = (pq_c1 + pq_c2 * Lm) / (1.0f + pq_c3 * Lm);
            N = Pow(N, pq_m2);
            return N;
        }

        Vector3 pq_r_f3(Vector3 In)
        {
            // converts from linear cd/m^2 to PQ code values

            Vector3 Out;
            Out.x = pq_r(In[0]);
            Out.y = pq_r(In[1]);
            Out.z = pq_r(In[2]);

            return Out;
        }

        Vector3 pq_f_f3(Vector3 In)
        {
            // converts from PQ code values to linear cd/m^2

            Vector3 Out;
            Out.x = pq_f(In[0]);
            Out.y = pq_f(In[1]);
            Out.z = pq_f(In[2]);

            return Out;
        }

        float glow_fwd(float ycIn, float glowGainIn, float glowMid)
        {
            float glowGainOut;

            if (ycIn <= 2.0f / 3.0f * glowMid)
            {
                glowGainOut = glowGainIn;
            }
            else if (ycIn >= 2.0f * glowMid)
            {
                glowGainOut = 0.0f;
            }
            else
            {
                glowGainOut = glowGainIn * (glowMid / ycIn - 1.0f / 2.0f);
            }

            return glowGainOut;
        }

        float cubic_basis_shaper(float x, float w   /* full base width of the shaper function (in degrees)*/)
        {
            var M = new Vector4[]
            {
                new Vector4(-1.0f / 6, 3.0f / 6, -3.0f / 6, 1.0f / 6 ),
                new Vector4( 3.0f / 6, -6.0f / 6, 3.0f / 6, 0.0f / 6 ),
                new Vector4(-3.0f / 6, 0.0f / 6, 3.0f / 6, 0.0f / 6 ),
                new Vector4( 1.0f / 6, 4.0f / 6, 1.0f / 6, 0.0f / 6)
            };

            var knots = new[]
            {
                -w / 2.0f,
                -w / 4.0f,
                0.0f,
                w / 4.0f,
                w / 2.0f
            };

            // EHart - init y, because CTL does by default
            float y = 0;
            if ((x > knots[0]) && (x < knots[4]))
            {
                float knot_coord = (x - knots[0]) * 4.0f / w;
                int j = (int)(knot_coord);
                float t = knot_coord - j;

                var monomials = new float[] { t * t * t, t * t, t, 1.0f };

                // (if/else structure required for compatibility with CTL < v1.5.)
                if (j == 3)
                {
                    y = monomials[0] * M[0][0] + monomials[1] * M[1][0] +
                        monomials[2] * M[2][0] + monomials[3] * M[3][0];
                }
                else if (j == 2)
                {
                    y = monomials[0] * M[0][1] + monomials[1] * M[1][1] +
                        monomials[2] * M[2][1] + monomials[3] * M[3][1];
                }
                else if (j == 1)
                {
                    y = monomials[0] * M[0][2] + monomials[1] * M[1][2] +
                        monomials[2] * M[2][2] + monomials[3] * M[3][2];
                }
                else if (j == 0)
                {
                    y = monomials[0] * M[0][3] + monomials[1] * M[1][3] +
                        monomials[2] * M[2][3] + monomials[3] * M[3][3];
                }
                else
                {
                    y = 0.0f;
                }
            }

            return y * 3 / 2.0f;
        }

        float sign(float x)
        {
            if (x < 0.0f)
                return -1.0f;
            if (x > 0.0f)
                return 1.0f;
            return 0.0f;
        }


        float sigmoid_shaper(float x)
        {
            // Sigmoid function in the range 0 to 1 spanning -2 to +2.

            float t = Max(1.0f - Abs(x / 2.0f), 0.0f);
            float y = 1.0f + sign(x) * (1.0f - t * t);

            return y / 2.0f;
        }

        float center_hue(float hue, float centerH)
        {
            float hueCentered = hue - centerH;
            if (hueCentered < -180.0f) hueCentered = hueCentered + 360.0f;
            else if (hueCentered > 180.0f) hueCentered = hueCentered - 360.0f;
            return hueCentered;
        }

        Vector3 rrt(Vector3 rgbIn)
        {
            // "Glow" module constants
            const float RRT_GLOW_GAIN = 0.05f;
            const float RRT_GLOW_MID = 0.08f;
            // --- Glow module --- //
            float saturation = rgb_2_saturation(rgbIn);
            float ycIn = rgb_2_yc(rgbIn);
            float s = sigmoid_shaper((saturation - 0.4f) / 0.2f);
            float addedGlow = 1.0f + glow_fwd(ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);

            Vector3 aces = addedGlow * rgbIn;

            // Red modifier constants
            const float RRT_RED_SCALE = 0.82f;
            const float RRT_RED_PIVOT = 0.03f;
            const float RRT_RED_HUE = 0.0f;
            const float RRT_RED_WIDTH = 135.0f;
            // --- Red modifier --- //
            float hue = rgb_2_hue(aces);
            float centeredHue = center_hue(hue, RRT_RED_HUE);
            float hueWeight = cubic_basis_shaper(centeredHue, RRT_RED_WIDTH);

            aces[0] = aces[0] + hueWeight * saturation * (RRT_RED_PIVOT - aces[0]) * (1.0f - RRT_RED_SCALE);

            // --- ACES to RGB rendering space --- //
            aces = Max(aces, 0.0f);  // avoids saturated negative colors from becoming positive in the matrix

            Vector3 rgbPre = Mul(AP0_2_AP1_MAT, aces);

            rgbPre = Clamp(rgbPre, 0.0f, HALF_MAX);

            // Desaturation contants
            float RRT_SAT_FACTOR = 0.96f;
            Matrix3x3 RRT_SAT_MAT = calc_sat_adjust_matrix(RRT_SAT_FACTOR, AP1_RGB2Y);
            // --- Global desaturation --- //
            rgbPre = Mul(RRT_SAT_MAT, rgbPre);

            // --- Apply the tonescale independently in rendering-space RGB --- //
            Vector3 rgbPost;
            rgbPost.x = segmented_spline_c5_fwd(rgbPre[0]);
            rgbPost.y = segmented_spline_c5_fwd(rgbPre[1]);
            rgbPost.z = segmented_spline_c5_fwd(rgbPre[2]);

            // --- RGB rendering space to OCES --- //
            Vector3 rgbOces = Mul(AP1_2_AP0_MAT, rgbPost);

            return rgbOces;
        }

        float pow10(float x)
        {
            return Pow(10.0f, x);
        }

        float segmented_spline_c9_fwd(float x, SegmentedSplineParams_c9 C)
        {
            var M = new Matrix3x3(0.5f, -1.0f, 0.5f, -1.0f, 1.0f, 0.5f, 0.5f, 0.0f, 0.0f);

            const int N_KNOTS_LOW = 8;
            const int N_KNOTS_HIGH = 8;

            // Check for negatives or zero before taking the log. If negative or zero,
            // set to OCESMIN.
            float xCheck = x;
            if (xCheck <= 0.0) xCheck = 1e-4f;

            float logx = Log10(xCheck);

            float logy;

            if (logx <= Log10(C.minPoint.x))
            {

                logy = logx * C.slope.x + (Log10(C.minPoint.y) - C.slope.x * Log10(C.minPoint.x));

            }
            else if ((logx > Log10(C.minPoint.x)) && (logx < Log10(C.midPoint.x)))
            {

                float knot_coord = (N_KNOTS_LOW - 1) * (logx - Log10(C.minPoint.x)) / (Log10(C.midPoint.x) - Log10(C.minPoint.x));
                int j = (int)(knot_coord);
                float t = knot_coord - j;

                Vector3 cf = new(C.coefs[j].x, C.coefs[j + 1].x, C.coefs[j + 2].x);

                Vector3 monomials = new(t * t, t, 1.0f);

                Vector3 basis = cf.x * M.GetRow(0) + cf.y * M.GetRow(1) + cf.z * M.GetRow(2);
                logy = Vector3.Dot(monomials, basis);

            }
            else if ((logx >= Log10(C.midPoint.x)) && (logx < Log10(C.maxPoint.x)))
            {

                float knot_coord = (N_KNOTS_HIGH - 1) * (logx - Log10(C.midPoint.x)) / (Log10(C.maxPoint.x) - Log10(C.midPoint.x));
                int j = (int)(knot_coord);
                float t = knot_coord - j;

                Vector3 cf = new(C.coefs[j].y, C.coefs[j + 1].y, C.coefs[j + 2].y);

                Vector3 monomials = new Vector3(t * t, t, 1.0f);

                Vector3 basis = cf.x * M.GetRow(0) + cf.y * M.GetRow(1) + cf.z * M.GetRow(2);
                logy = Vector3.Dot(monomials, basis);
            }
            else
            { //if ( logIn >= Log10(C.maxPoint.x) ) { 

                logy = logx * C.slope.y + (Log10(C.maxPoint.y) - C.slope.y * Log10(C.maxPoint.x));

            }

            return pow10(logy);
        }

        static Matrix3x3 D65_2_D60_CAT = new
        (
            1.01303f, 0.00610531f, -0.014971f,
            0.00769823f, 0.998165f, -0.00503203f,
            -0.00284131f, 0.00468516f, 0.924507f
        );

        static Matrix3x3 sRGB_2_XYZ_MAT = new
        (
            0.41239089f, 0.35758430f, 0.18048084f,
            0.21263906f, 0.71516860f, 0.07219233f,
            0.01933082f, 0.11919472f, 0.95053232f
        );

        const float DISPGAMMA = 2.4f;
        const float OFFSET = 0.055f;

        //  EvalACES
        Vector3 EvalACES(Vector3 InColor, ACESparams Params)
        {
            Vector3 aces = Mul(XYZ_2_AP0_MAT, Mul(D65_2_D60_CAT, Mul(sRGB_2_XYZ_MAT, InColor)));

            Vector3 oces = rrt(aces);

            // OCES to RGB rendering space
            Vector3 rgbPre = Mul(AP0_2_AP1_MAT, oces);


            Vector3 rgbPost;

            if (Params.tonemapLuminance)
            {
                // luminance only path, for content that has been mastered for an expectation of an oversaturated tonemap operator
                float y = Vector3.Dot(rgbPre, AP1_RGB2Y);
                float scale = segmented_spline_c9_fwd(y, Params.C) / y;

                // compute the more desaturated per-channel version
                rgbPost.x = segmented_spline_c9_fwd(rgbPre[0], Params.C);
                rgbPost.y = segmented_spline_c9_fwd(rgbPre[1], Params.C);
                rgbPost.z = segmented_spline_c9_fwd(rgbPre[2], Params.C);

                // lerp between values
                rgbPost = Max(Vector3.Lerp(rgbPost, rgbPre * scale, Params.saturationLevel), Params.CinemaLimits.x); // clamp to Min to prevent the genration of negative values
            }
            else
            {
                // Apply the tonescale independently in rendering-space RGB
                rgbPost.x = segmented_spline_c9_fwd(rgbPre[0], Params.C);
                rgbPost.y = segmented_spline_c9_fwd(rgbPre[1], Params.C);
                rgbPost.z = segmented_spline_c9_fwd(rgbPre[2], Params.C);
            }

            // Scale luminance to linear code value
            Vector3 linearCV;
            linearCV.x = Y_2_linCV(rgbPost[0], Params.CinemaLimits.y, Params.CinemaLimits.x);
            linearCV.y = Y_2_linCV(rgbPost[1], Params.CinemaLimits.y, Params.CinemaLimits.x);
            linearCV.z = Y_2_linCV(rgbPost[2], Params.CinemaLimits.y, Params.CinemaLimits.x);

            if (Params.surroundAdjust)
            {
                // Apply gamma adjustment to compensate for surround
                linearCV = alter_surround(linearCV, Params.surroundGamma);
            }

            if (Params.desaturate)
            {
                // Apply desaturation to compensate for luminance difference
                // Saturation compensation factor
                const float ODT_SAT_FACTOR = 0.93f;
                Matrix3x3 ODT_SAT_MAT = calc_sat_adjust_matrix(ODT_SAT_FACTOR, AP1_RGB2Y);
                linearCV = Mul(ODT_SAT_MAT, linearCV);
            }

            // Convert to display primary encoding
            // Rendering space RGB to XYZ
            Vector3 XYZ = Mul(AP1_2_XYZ_MAT, linearCV);

            if (Params.applyCAT)
            {
                // Apply CAT from ACES white point to assumed observer adapted white point
                // EHart - should recompute this matrix
                Matrix3x3 D60_2_D65_CAT = new
                (
                    0.987224f, -0.00611327f, 0.0159533f,
                    -0.00759836f, 1.00186f, 0.00533002f,
                    0.00307257f, -0.00509595f, 1.08168f
                );

                XYZ = Mul(D60_2_D65_CAT, XYZ);
            }

            // CIE XYZ to display primaries
            linearCV = Mul(Params.XYZ_2_DISPLAY_PRI_MAT, XYZ);

            // Encode linear code values with transfer function
            Vector3 outputCV = linearCV;

            if (Params.OutputMode == 0)
            {
                // LDR mode, clamp 0/1 and encode 
                linearCV = Clamp(linearCV, 0.0f, 1.0f);

                outputCV[0] = moncurve_r(linearCV[0], DISPGAMMA, OFFSET);
                outputCV[1] = moncurve_r(linearCV[1], DISPGAMMA, OFFSET);
                outputCV[2] = moncurve_r(linearCV[2], DISPGAMMA, OFFSET);
            }
            else if (Params.OutputMode == 1)
            {
                //scale to bring the ACES data back to the proper range
                linearCV[0] = linCV_2_Y(linearCV[0], Params.CinemaLimits.y, Params.CinemaLimits.x);
                linearCV[1] = linCV_2_Y(linearCV[1], Params.CinemaLimits.y, Params.CinemaLimits.x);
                linearCV[2] = linCV_2_Y(linearCV[2], Params.CinemaLimits.y, Params.CinemaLimits.x);

                // Handle out-of-gamut values
                // Clip values < 0 (i.e. projecting outside the display primaries)
                //rgb = clamp(rgb, 0., HALF_POS_INF);
                linearCV = Max(linearCV, 0.0f);

                // Encode with PQ transfer function
                outputCV = pq_r_f3(linearCV);
            }
            else if (Params.OutputMode == 2)
            {
                // scRGB

                //scale to bring the ACES data back to the proper range
                linearCV[0] = linCV_2_Y(linearCV[0], Params.CinemaLimits.y, Params.CinemaLimits.x);
                linearCV[1] = linCV_2_Y(linearCV[1], Params.CinemaLimits.y, Params.CinemaLimits.x);
                linearCV[2] = linCV_2_Y(linearCV[2], Params.CinemaLimits.y, Params.CinemaLimits.x);

                // Handle out-of-gamut values
                // Clip values < 0 (i.e. projecting outside the display primaries)
                //rgb = clamp(rgb, 0., HALF_POS_INF);
                linearCV = Max(linearCV, 0.0f);


                Matrix3x3 XYZ_2_sRGB_MAT = new
                (
                    3.24096942f, -1.53738296f, -0.49861076f,
                    -0.96924388f, 1.87596786f, 0.04155510f,
                    0.05563002f, -0.20397684f, 1.05697131f
                );

                // convert from eported display primaries to sRGB primaries
                linearCV = Mul(Params.DISPLAY_PRI_MAT_2_XYZ, linearCV);
                linearCV = Mul(XYZ_2_sRGB_MAT, linearCV);

                // map 1.0 to 80 nits (or Max nit level if it is lower)
                outputCV = linearCV * (1.0f / Min(80.0f, Params.CinemaLimits.y));
            }

            return outputCV;
        }
    }
}

