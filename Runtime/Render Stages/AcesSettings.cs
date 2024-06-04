using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    [Serializable]
    public class AcesSettings
    {
        public ColorSpace ColorSpace = ColorSpace.Rec709;

        public ODTCurve ToneCurve = ODTCurve.ODT_LDR_Ref; // Tone Curve

        public EOTF EOTF = EOTF.scRGB; // scRGB

        [Range(-14.0f, 0.0f)] public float minStops = 0.0f;

        [Range(0.0f, 20.0f)] public float maxStops = 8.0f;

        [Range(-1.0f, 4000.0f)] public float maxLevel = -1.0f; // "Max Level (nits -1=default)"

        [Range(0.01f, 100.0f)] public float midGrayScale = 1.0f; // "Middle Gray Scale"

        [Range(0.6f, 1.2f)] public float surroundGamma = 0.9811f; // "Surround Gamma"

        [Range(0.01f, 1.1f)] public float toneCurveSaturation = 1.0f; // "Saturation level"

        [Range(0.02f, 4.0f)] public float outputGamma = 2.2f; // EOTF Mode/Gamma value

        public bool adjustWP = true; // "CAT D60 to D65"

        public bool desaturate = true; // "Desaturate"

        public bool dimSurround = true; // "Alter Surround"

        public bool luminanceOnly = false; // "Tonemap Luminance"

        public AcesSettings()
        {
            EOTF = EOTF.scRGB; // scRGB
            surroundGamma = 0.9811f;
            toneCurveSaturation = 1.0f;
            ColorSpace = ColorSpace.Rec709;
            ToneCurve = ODTCurve.ODT_LDR_Ref;
            outputGamma = 2.2f;
            adjustWP = true;
            desaturate = true;
            dimSurround = true;
            luminanceOnly = false;
            maxStops = 8.0f;
            maxLevel = -1.0f;
            midGrayScale = 1.0f;
        }

        void Apply1000nitHDR()
        {
            ToneCurve = ODTCurve.ODT_1000Nit_Adj;
            maxStops = -12.0f;
            maxStops = 10.0f;
            midGrayScale = 1.0f;
            adjustWP = true;
            desaturate = false;
            ColorSpace = ColorSpace.BT2020;
            EOTF = EOTF.scRGB; // scRGB
        }

        void Apply1000nitHDRSharpened()
        {
            ToneCurve = ODTCurve.ODT_1000Nit_Adj;
            minStops = -8.0f;
            maxStops = 8.0f;
            midGrayScale = 1.0f;
            adjustWP = true;
            desaturate = false;
            ColorSpace = ColorSpace.BT2020;
            EOTF = EOTF.scRGB; // scRGB
        }

        void ApplySDR()
        {
            ToneCurve = ODTCurve.ODT_LDR_Adj;
            minStops = -6.5f;
            maxStops = 6.5f;
            midGrayScale = 1.0f;
            adjustWP = true;
            desaturate = true;
            ColorSpace = ColorSpace.Rec709;
            EOTF = EOTF.sRGB; // sRGB
        }

        void ApplyEDRExtreme()
        {
            ToneCurve = ODTCurve.ODT_1000Nit_Adj;
            minStops = -12.0f;
            maxStops = 9.0f;
            midGrayScale = 1.0f;
            adjustWP = true;
            desaturate = false;
            ColorSpace = ColorSpace.Rec709;
            EOTF = EOTF.sRGB; // sRGB
        }

        void ApplyEDR()
        {
            ToneCurve = ODTCurve.ODT_1000Nit_Adj;
            minStops = -8.0f;
            maxStops = 8.0f;
            midGrayScale = 3.0f;
            adjustWP = true;
            desaturate = false;
            ColorSpace = ColorSpace.Rec709;
            EOTF = EOTF.sRGB; // sRGB
        }
    };
}