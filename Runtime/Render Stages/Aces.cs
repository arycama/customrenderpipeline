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
        static SegmentedSplineParams_c9_internal ODT_48nits => new SegmentedSplineParams_c9_internal
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
        static SegmentedSplineParams_c9_internal ODT_1000nits => new SegmentedSplineParams_c9_internal
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
        static SegmentedSplineParams_c9_internal ODT_2000nits => new SegmentedSplineParams_c9_internal
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
        static SegmentedSplineParams_c9_internal ODT_4000nits => new SegmentedSplineParams_c9_internal
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
    }
}

