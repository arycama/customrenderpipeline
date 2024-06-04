using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    [Serializable]
    public class AcesSettings
    {
        public ColorSpace selectedColorMatrix = ColorSpace.Rec709; // Color Space

        public ODTCurve selectedCurve = ODTCurve.ODT_LDR_Ref; // Tone Curve

        public EOTF outputMode = EOTF.scRGB; // scRGB

        [Range(-14.0f, 0.0f)] public float minStops = 0.0f;

        [Range(0.0f, 14.0f)] public float maxStops = 8.0f;

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
            outputMode = EOTF.scRGB; // scRGB
            surroundGamma = 0.9811f;
            toneCurveSaturation = 1.0f;
            selectedColorMatrix = ColorSpace.Rec709;
            selectedCurve = ODTCurve.ODT_LDR_Ref;
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
            selectedCurve = ODTCurve.ODT_1000Nit_Adj;
            maxStops = -12.0f;
            maxStops = 10.0f;
            midGrayScale = 1.0f;
            adjustWP = true;
            desaturate = false;
            selectedColorMatrix = ColorSpace.BT2020;
            outputMode = EOTF.scRGB; // scRGB
        }

        void Apply1000nitHDRSharpened()
        {
            selectedCurve = ODTCurve.ODT_1000Nit_Adj;
            minStops = -8.0f;
            maxStops = 8.0f;
            midGrayScale = 1.0f;
            adjustWP = true;
            desaturate = false;
            selectedColorMatrix = ColorSpace.BT2020;
            outputMode = EOTF.scRGB; // scRGB
        }

        void ApplySDR()
        {
            selectedCurve = ODTCurve.ODT_LDR_Adj;
            minStops = -6.5f;
            maxStops = 6.5f;
            midGrayScale = 1.0f;
            adjustWP = true;
            desaturate = true;
            selectedColorMatrix = ColorSpace.Rec709;
            outputMode = EOTF.sRGB; // sRGB
        }

        void ApplyEDRExtreme()
        {
            selectedCurve = ODTCurve.ODT_1000Nit_Adj;
            minStops = -12.0f;
            maxStops = 9.0f;
            midGrayScale = 1.0f;
            adjustWP = true;
            desaturate = false;
            selectedColorMatrix = ColorSpace.Rec709;
            outputMode = EOTF.sRGB; // sRGB
        }

        void ApplyEDR()
        {
            selectedCurve = ODTCurve.ODT_1000Nit_Adj;
            minStops = -8.0f;
            maxStops = 8.0f;
            midGrayScale = 3.0f;
            adjustWP = true;
            desaturate = false;
            selectedColorMatrix = ColorSpace.Rec709;
            outputMode = EOTF.sRGB; // sRGB
        }
    };
}