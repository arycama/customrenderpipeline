using System;
using UnityEngine;
using static Arycama.CustomRenderPipeline.MathUtils;
using static System.MathF;

namespace Arycama.CustomRenderPipeline
{
    public class Aces
    {
        private const float MiddleGrey = 0.18f;

        // Precomputed curves
        // ODT_48nits
        private static SegmentedSplineParamsC9 ODT_48nits => new SegmentedSplineParamsC9
        (
            new[] { -1.6989700043f, -1.6989700043f, -1.4779000000f, -1.2291000000f, -0.8648000000f, -0.4480000000f, 0.0051800000f, 0.4511080334f, 0.9113744414f, 0.9113744414f },
            new[] { 0.5154386965f, 0.8470437783f, 1.1358000000f, 1.3802000000f, 1.5197000000f, 1.5985000000f, 1.6467000000f, 1.6746091357f, 1.6878733390f, 1.6878733390f },
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey * Exp2(-6.5f)), 0.02f), //-65 = 0.01 nits
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey), 4.8f),
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey * Exp2(6.5f)), 48.0f), // 6.5 = ~90 nits
            new Vector2(0.0f, 0.04f),
            new Vector2(MiddleGrey * Exp2(-6.5f), MiddleGrey * Exp2(6.5f))
        );

        // ODT_1000nits
        private static SegmentedSplineParamsC9 ODT_1000nits => new SegmentedSplineParamsC9
        (
            new[] { -2.3010299957f, -2.3010299957f, -1.9312000000f, -1.5205000000f, -1.0578000000f, -0.4668000000f, 0.1193800000f, 0.7088134201f, 1.2911865799f, 1.2911865799f },
            new[] { 0.8089132070f, 1.1910867930f, 1.5683000000f, 1.9483000000f, 2.3083000000f, 2.6384000000f, 2.8595000000f, 2.9872608805f, 3.0127391195f, 3.0127391195f },
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey * Exp2(-12.0f)), 0.005f), //-12 = 0.0002 nits
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey), 10.0f),
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey * Exp2(10.0f)), 1000.0f), // 1024 nits
            new Vector2(0.0f, 0.06f),
            new Vector2(MiddleGrey * Exp2(-12.0f), MiddleGrey * Exp2(10.0f))
        );

        // ODT_2000nits
        private static SegmentedSplineParamsC9 ODT_2000nits => new SegmentedSplineParamsC9
        (
            new[] { -2.3010299957f, -2.3010299957f, -1.9312000000f, -1.5205000000f, -1.0578000000f, -0.4668000000f, 0.1193800000f, 0.7088134201f, 1.2911865799f, 1.2911865799f },
            new[] { 0.8019952042f, 1.1980047958f, 1.5943000000f, 1.9973000000f, 2.3783000000f, 2.7684000000f, 3.0515000000f, 3.2746293562f, 3.3274306351f, 3.3274306351f },
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey * Exp2(-12.0f)), 0.005f), //-12 = 0.0002 nits
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey), 10.0f),
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey * Exp2(11.0f)), 2000.0f), // 11 = 2048 nits
            new Vector2(0.0f, 0.12f),
            new Vector2(MiddleGrey * Exp2(-12.0f), MiddleGrey * Exp2(11.0f))
        );

        // ODT_4000nits
        private static SegmentedSplineParamsC9 ODT_4000nits => new SegmentedSplineParamsC9
        (
            new[] { -2.3010299957f, -2.3010299957f, -1.9312000000f, -1.5205000000f, -1.0578000000f, -0.4668000000f, 0.1193800000f, 0.7088134201f, 1.2911865799f, 1.2911865799f },
            new[] { 0.7973186613f, 1.2026813387f, 1.6093000000f, 2.0108000000f, 2.4148000000f, 2.8179000000f, 3.1725000000f, 3.5344995451f, 3.6696204376f, 3.6696204376f },
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey * Exp2(-12.0f)), 0.005f),
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey), 10.0f),
            new Vector2(SegmentedSplineC5Fwd(MiddleGrey * Exp2(12.0f)), 4000.0f), // 12 = 4096 nits
            new Vector2(0.0f, 0.3f),
            new Vector2(MiddleGrey * Exp2(-12.0f), MiddleGrey * Exp2(12.0f))
        );

        private struct SegmentedSplineParamsC5
        {
            public float[] coefsLow;    // coefs for B-spline between minPoint and midPoint (units of log luminance)
            public float[] coefsHigh;   // coefs for B-spline between midPoint and maxPoint (units of log luminance)
            public Vector2 minPoint; // {luminance, luminance} linear extension below this
            public Vector2 midPoint; // {luminance, luminance} 
            public Vector2 maxPoint; // {luminance, luminance} linear extension above this
            public float slopeLow;       // log-log slope of low linear extension
            public float slopeHigh;      // log-log slope of high linear extension

            public SegmentedSplineParamsC5(float[] coefsLow, float[] coefsHigh, Vector2 minPoint, Vector2 midPoint, Vector2 maxPoint, float slopeLow, float slopeHigh)
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

        public struct SegmentedSplineParamsC9
        {
            public float[] coefsLow;    // coefs for B-spline between minPoint and midPoint (units of log luminance)
            public float[] coefsHigh;   // coefs for B-spline between midPoint and maxPoint (units of log luminance)
            public Vector2 minPoint; // {luminance, luminance} linear extension below this
            public Vector2 midPoint; // {luminance, luminance} 
            public Vector2 maxPoint; // {luminance, luminance} linear extension above this
            public Vector2 slope;
            public Vector2 limits; // Min and Max prior to RRT

            public SegmentedSplineParamsC9(float[] coefsLow, float[] coefsHigh, Vector2 minPoint, Vector2 midPoint, Vector2 maxPoint, Vector2 slope, Vector2 limits)
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
        private static float SegmentedSplineC5Fwd(float x)
        {
            // RRT_PARAMS
            SegmentedSplineParamsC5 C = new(
                new float[] { -4.0000000000f, -4.0000000000f, -3.1573765773f, -0.4852499958f, 1.8477324706f, 1.8477324706f },
                new float[] { -0.7185482425f, 2.0810307172f, 3.6681241237f, 4.0000000000f, 4.0000000000f, 4.0000000000f },
                new Vector2(MiddleGrey * Exp2(-15.0f), 0.0001f),
                new Vector2(MiddleGrey, 4.8f),
                new Vector2(MiddleGrey * Exp2(18.0f), 10000.0f),
                0.0f,
                0.0f);

            // Textbook monomial to basis-function conversion matrix.
            Matrix3x3 M = new(0.5f, -1.0f, 0.5f, -1.0f, 1.0f, 0.5f, 0.5f, 0.0f, 0.0f);

            const int N_KNOTS_LOW = 4;
            const int N_KNOTS_HIGH = 4;

            // Check for negatives or zero before taking the log. If negative or zero,
            // set to ACESMIN.1
            var xCheck = x <= 0 ? Exp2(-14.0f) : x;

            var logx = Log10(xCheck);
            float logy;

            if (logx <= Log10(C.minPoint.x))
            {
                logy = logx * C.slopeLow + (Log10(C.minPoint.y) - C.slopeLow * Log10(C.minPoint.x));
            }
            else if ((logx > Log10(C.minPoint.x)) && (logx < Log10(C.midPoint.x)))
            {
                var knot_coord = (N_KNOTS_LOW - 1) * (logx - Log10(C.minPoint.x)) / (Log10(C.midPoint.x) - Log10(C.minPoint.x));
                var j = (int)(knot_coord);
                var t = knot_coord - j;

                Vector3 cf = new(C.coefsLow[j], C.coefsLow[j + 1], C.coefsLow[j + 2]);

                Vector3 monomials = new(t * t, t, 1);
                //logy = dot(monomials, Mul(cf, M));
                //logy = Vector3.DotProduct(monomials, M.TransformVector(cf));
                var basis = cf.x * M.GetRow(0) + cf.y * M.GetRow(1) + cf.z * M.GetRow(2);
                logy = Vector3.Dot(monomials, basis);
            }
            else if ((logx >= Log10(C.midPoint.x)) && (logx < Log10(C.maxPoint.x)))
            {
                var knot_coord = (N_KNOTS_HIGH - 1) * (logx - Log10(C.midPoint.x)) / (Log10(C.maxPoint.x) - Log10(C.midPoint.x));
                var j = (int)(knot_coord);
                var t = knot_coord - j;

                Vector3 cf = new(C.coefsHigh[j], C.coefsHigh[j + 1], C.coefsHigh[j + 2]);

                Vector3 monomials = new(t * t, t, 1);
                //logy = dot(monomials, Mul(cf, M));
                //logy = Vector3.DotProduct(monomials, M.TransformVector(cf));
                var basis = cf.x * M.GetRow(0) + cf.y * M.GetRow(1) + cf.z * M.GetRow(2);
                logy = Vector3.Dot(monomials, basis);
            }
            else
            { //if ( logIn >= Log10(C.maxPoint.x) ) { 
                logy = logx * C.slopeHigh + (Log10(C.maxPoint.y) - C.slopeHigh * Log10(C.maxPoint.x));
            }

            return Exp10(logy);
        }

        private static void mul3(out Vector3 res, Vector3 a, Matrix3x3 M)
        {
            res = a.x * M.GetRow(0) + a.y * M.GetRow(1) + a.z * M.GetRow(2);
        }

        // Using a reference ODT spline, adjust middle gray and Max levels
        private static SegmentedSplineParamsC9 AdaptSpline(SegmentedSplineParamsC9 C, float minLuminance, float paperWhite, float maxLuminance, float outMax)
        {
            // Monomial and inverse monomial matrices
            var M = new Matrix3x3(0.5f, -1.0f, 0.5f, -1.0f, 1.0f, 0.5f, 0.5f, 0.0f, 0.0f);
            var iM = new Matrix3x3(0.0f, 0.0f, 2.0f, -0.5f, 0.5f, 1.5f, 1.0f, 1.0f, 1.0f);

            const int N_KNOTS_LOW = 8;
            const int N_KNOTS_HIGH = 8;

            var C2 = C;

            // Set the new Max input and output levels
            C2.maxPoint.x = SegmentedSplineC5Fwd(maxLuminance * MiddleGrey);
            C2.maxPoint.y = outMax;

            C2.limits.y = maxLuminance * MiddleGrey;

            // Set new minimum levels
            C2.minPoint.x = SegmentedSplineC5Fwd(minLuminance * MiddleGrey);

            C2.limits.x = minLuminance * MiddleGrey;

            // scale the middle gray output level
            var paperMiddleGrey = MiddleGrey * paperWhite;
            C2.midPoint.y *= paperMiddleGrey / C2.midPoint.y;

            // compute and apply scale used to bring bottom segment of the transform to the level desired for middle gray
            var scale = (Log10(C.midPoint[1]) - Log10(C.minPoint[1])) / (Log10(C2.midPoint[1]) - Log10(C2.minPoint[1]));

            for (var j = 0; j < N_KNOTS_LOW + 2; j++)
            {
                C2.coefsLow[j] = (C2.coefsLow[j] - Log10(C2.minPoint[1])) / scale + Log10(C2.minPoint[1]);
            }

            // compute and apply scale to top segment of the transform to the level matching the new Max and middle gray
            scale = (Log10(C.maxPoint[1]) - Log10(C.midPoint[1])) / (Log10(C2.maxPoint[1]) - Log10(C2.midPoint[1]));

            var target = new float[10]; // saves the "target" values, as we need to match/relax the spline to properly join the low segment

            for (var j = 0; j < N_KNOTS_HIGH + 2; j++)
            {
                C2.coefsHigh[j] = (C2.coefsHigh[j] - Log10(C.midPoint[1])) / scale + Log10(C2.midPoint[1]);
                target[j] = C2.coefsHigh[j];
            }

            // Adjust the high spline to properly meet the low spline, then relax the high spline

            // Coefficients for the last segment of the low range
            Vector3 cfl = new(C2.coefsLow[7], C2.coefsLow[8], C2.coefsLow[9]);

            // Coeffiecients for the first segment of the high range
            Vector3 cfh = new(C2.coefsHigh[0], C2.coefsHigh[1], C2.coefsHigh[2]);


            // transform the coefficients by the monomial matrix
            mul3(out var cflt, cfl, M);
            mul3(out var cfht, cfh, M);

            // low and high curves cover different ranges, so compute scaling factor needed to match slopes at the join point
            var scaleLow = 1.0f / (Log10(C2.midPoint[0]) - Log10(C2.minPoint[0]));
            var scaleHigh = 1.0f / (Log10(C2.maxPoint[0]) - Log10(C2.midPoint[0]));

            // compute the targeted exit point for the segment 
            var outRef = cfht[0] * 2.0f + cfht[1]; //slope at t == 1

            // match slopes and intersection
            cfht[2] = cflt[2];
            cfht[1] = (scaleLow * cflt[1]) / scaleHigh;

            // compute the exit point the segment has after the adjustment
            var o = cfht[0] * 2.0f + cfht[1]; //result at t == 1

            // ease spline toward target and adjust
            var outTarget = (outRef * 7.0f + o * 1.0f) / 8.0f;
            cfht[0] = (outTarget - cfht[1]) / 2.0f;

            // back-transform  the adjustments and save them

            mul3(out var acfh, cfht, iM);

            C2.coefsHigh[0] = acfh[0];
            C2.coefsHigh[1] = acfh[1];
            C2.coefsHigh[2] = acfh[2];

            // now correct the rest of the spline
            for (var j = 1; j < N_KNOTS_HIGH; j++)
            {
                //  Original rescaled spline values for the segment (ideal "target")
                Vector3 cfoh = new(target[j], target[j + 1], target[j + 2]);

                //  New spline values for the segment based on alterations to prior ranges
                Vector3 cfh1 = new(C2.coefsHigh[j], C2.coefsHigh[j + 1], C2.coefsHigh[j + 2]);


                mul3(out var cfht1, cfh1, M);
                mul3(out var cfoht1, cfoh, M);

                //Compute exit slope for segments
                var o1 = cfht1[0] * 2.0f + cfht1[1]; //slope at t == 1
                var outRef1 = cfoht1[0] * 2.0f + cfoht1[1]; //slope at t == 1

                //Ease spline toward targetted slope
                var outTarget1 = (outRef1 * (7.0f - j) + o1 * (1.0f + j)) / 8.0f;
                cfht1[0] = (outTarget1 - cfht1[1]) / 2.0f;

                // Back transform and save

                mul3(out var acfh1, cfht1, iM);

                C2.coefsHigh[j] = acfh1[0];
                C2.coefsHigh[j + 1] = acfh1[1];
                C2.coefsHigh[j + 2] = acfh1[2];
            }

            return C2;
        }



        public static SegmentedSplineParamsC9 GetAcesODTData(ODTCurve baseCurve, float minLuminance, float paperWhite, float maxLuminance, float outMax)
        {
            // Select a curve used as part of the ACES ODT. Optionally, derive a modified version of the base curve with an altered middle gray and maximum number of stops.
            return baseCurve switch
            {
                ODTCurve.RefLdr => ODT_48nits,
                ODTCurve.Ref1000Nit => ODT_1000nits,
                ODTCurve.Ref2000Nit => ODT_2000nits,
                ODTCurve.Ref4000Nit => ODT_4000nits,
                ODTCurve.AdjLdr => AdaptSpline(ODT_48nits, minLuminance, paperWhite, maxLuminance, outMax),
                ODTCurve.Adj1000Nit => AdaptSpline(ODT_1000nits, minLuminance, paperWhite, maxLuminance, outMax),
                ODTCurve.Adj2000Nit => AdaptSpline(ODT_2000nits, minLuminance, paperWhite, maxLuminance, outMax),
                ODTCurve.Adj4000Nit => AdaptSpline(ODT_4000nits, minLuminance, paperWhite, maxLuminance, outMax),
                _ => throw new ArgumentException(nameof(baseCurve)),
            };
        }
    }
}

