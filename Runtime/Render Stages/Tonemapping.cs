using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Arycama.CustomRenderPipeline
{
    public class Tonemapping : RenderFeature
    {
        private readonly Settings settings;
        private readonly Bloom.Settings bloomSettings;
        private readonly LensSettings lensSettings;
        private readonly Material material;

        [Serializable]
        public class Settings
        {
            [field: SerializeField, Range(0.0f, 1.0f)] public float NoiseIntensity { get; private set; } = 0.5f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float NoiseResponse { get; private set; } = 0.8f;
            [field: SerializeField] public Texture2D FilmGrainTexture { get; private set; } = null;

            [field: SerializeField] public bool Tonemap { get; private set; } = true;
            [field: SerializeField] public bool HdrEnabled { get; private set; } = true;
            [field: SerializeField] public bool AutoDetectValues { get; private set; } = true;
            [field: SerializeField, Min(0)] public int HdrMinNits { get; private set; } = 0;
            [field: SerializeField, Min(0)] public int HdrMaxNits { get; private set; } = 1000;
            [field: SerializeField, Min(0.0f)] public float PaperWhiteNits { get; private set; } = 300.0f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float SdrBrightness { get; private set; } = 0.5f;

            [field: SerializeField] public float MaxLuminance { get; private set; } = 1000.0f;
            [field: SerializeField] public ODTCurve ToneCurve { get; private set; } = ODTCurve.RefLdr;
        }

        public Tonemapping(Settings settings, Bloom.Settings bloomSettings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            this.bloomSettings = bloomSettings;
            this.lensSettings = lensSettings;
            material = new Material(Shader.Find("Hidden/Tonemapping")) { hideFlags = HideFlags.HideAndDontSave };

            var hdrInfo = HDROutputSettings.main;
            if (hdrInfo.available)
            {
                var gamut = hdrInfo.displayColorGamut;
                var primaries = ColorGamutUtility.GetColorPrimaries(gamut);
                var transfer = ColorGamutUtility.GetTransferFunction(gamut);
                var whitePoint = ColorGamutUtility.GetWhitePoint(gamut);

                Debug.Log($"HDR Display Info: Min Nits {hdrInfo.minToneMapLuminance}, Max Nits {hdrInfo.maxToneMapLuminance}, Paper White {hdrInfo.paperWhiteNits}, Max Full Frame Nits {hdrInfo.maxFullFrameToneMapLuminance}, Gamut {hdrInfo.displayColorGamut}, Primaries {primaries}, Transfer {transfer}, WhitePoint {whitePoint}");
            }
        }


        const float ReflectedLightMeterConstant = 12.5f;
        const float Sensitivity = 100.0f;

        public static float LuminanceToEV100(float luminance)
        {
            return MathUtils.Log2(luminance) * MathUtils.Log2(ReflectedLightMeterConstant / Sensitivity);
        }

        public static float Ev100ToLuminance(float ev100)
        {
            return MathUtils.Exp2(ev100) * (ReflectedLightMeterConstant / Sensitivity);
        }

        public void Render(RTHandle input, RTHandle bloom, RTHandle uITexture, bool isSceneView, int width, int height)
        {
            using var pass = renderGraph.AddRenderPass<BlitToScreenPass>("Tonemapping");
            pass.Initialize(material);
            pass.ReadTexture("_MainTex", input);
            pass.ReadTexture("_Bloom", bloom);
            pass.ReadTexture("UITexture", uITexture);
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();

            var hdrSettings = HDROutputSettings.main;
            var minNits = hdrSettings.available && settings.AutoDetectValues ? hdrSettings.minToneMapLuminance : settings.HdrMinNits;
            var maxNits = hdrSettings.available && settings.AutoDetectValues ? hdrSettings.maxToneMapLuminance : settings.HdrMaxNits;
            if (minNits < 0 || maxNits <= 0)
            {
                minNits = settings.HdrMinNits;
                maxNits = settings.HdrMaxNits;
            }

            var paperWhiteNits = hdrSettings.available && settings.AutoDetectValues ? hdrSettings.paperWhiteNits : settings.PaperWhiteNits;
            if (paperWhiteNits <= 0)
            {
                paperWhiteNits = settings.PaperWhiteNits;
                hdrSettings.paperWhiteNits = paperWhiteNits;
            }

            var acesSettingsBuffer = renderGraph.SetConstantBuffer(GetAcesConstants());
            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                pass.ReadBuffer("AcesConstants", acesSettingsBuffer);

                pass.SetFloat(command, "HdrEnabled", hdrSettings.available && settings.HdrEnabled ? 1.0f : 0.0f);
                pass.SetTexture(command, "_GrainTexture", data.grainTexture);

                pass.SetFloat(command, "_BloomStrength", data.bloomStrength);
                pass.SetFloat(command, "_IsSceneView", data.isSceneView);
                pass.SetFloat(command, "NoiseIntensity", data.noiseIntensity);
                pass.SetFloat(command, "NoiseResponse", data.noiseResponse);

                pass.SetFloat(command, "PaperWhiteNits", paperWhiteNits);
                pass.SetFloat(command, "SdrBrightness", settings.SdrBrightness);
                pass.SetFloat(command, "HdrMinNits", minNits);
                pass.SetFloat(command, "HdrMaxNits", maxNits);
                pass.SetFloat(command, "Tonemap", settings.Tonemap ? 1.0f : 0.0f);

                pass.SetFloat(command, "ShutterSpeed", data.shutterSpeed);
                pass.SetFloat(command, "Aperture", data.aperture);
                pass.SetVector(command, "_GrainTextureParams", data.grainTextureParams);
                pass.SetVector(command, "_Resolution", data.resolution);

                pass.SetVector(command, "_BloomScaleLimit", new Vector4(bloom.Scale.x, bloom.Scale.y, bloom.Limit.x, bloom.Limit.y));

                var colorGamut = hdrSettings.available ? hdrSettings.displayColorGamut : ColorGamut.sRGB;
                pass.SetInt(command, "ColorGamut", (int)colorGamut);
            });

            var offsetX = Random.value;
            var offsetY = Random.value;
            var uvScaleX = settings.FilmGrainTexture ? width / (float)settings.FilmGrainTexture.width : 1.0f;
            var uvScaleY = settings.FilmGrainTexture ? height / (float)settings.FilmGrainTexture.height : 1.0f;

            data.grainTexture = settings.FilmGrainTexture;
            data.bloomStrength = bloomSettings.Strength;
            data.isSceneView = isSceneView ? 1.0f : 0.0f;
            data.noiseIntensity = settings.NoiseIntensity;
            data.noiseResponse = settings.NoiseResponse;
            data.shutterSpeed = lensSettings.ShutterSpeed;
            data.aperture = lensSettings.Aperture;
            data.grainTextureParams = new Vector4(uvScaleX, uvScaleY, offsetX, offsetY);
            data.resolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
        }

        AcesConstants GetAcesConstants()
        {
            AcesConstants constants;

            // setup the aces data
            var sdrPaperWhiteNits = Mathf.Lerp(80, 480, settings.SdrBrightness);
            var paperWhite = HDROutputSettings.main.available && settings.HdrEnabled ? settings.PaperWhiteNits : sdrPaperWhiteNits;
            var aces = Aces.GetAcesODTData(settings.ToneCurve, settings.HdrMinNits, paperWhite, settings.HdrMaxNits, settings.MaxLuminance);

            constants.ACES_max = aces.maxPoint;
            constants.ACES_mid = aces.midPoint;
            constants.ACES_min = aces.minPoint;
            constants.ACES_slope = aces.slope;

            constants.CinemaLimits.x = aces.minPoint.y;
            constants.CinemaLimits.y = aces.maxPoint.y;
            constants.Padding = Vector2.zero;

            constants.coefLow0 = aces.coefsLow[0];
            constants.coefLow1 = aces.coefsLow[1];
            constants.coefLow2 = aces.coefsLow[2];
            constants.coefLow3 = aces.coefsLow[3];
            constants.coefLow4 = aces.coefsLow[4];
            constants.coefLow5 = aces.coefsLow[5];
            constants.coefLow6 = aces.coefsLow[6];
            constants.coefLow7 = aces.coefsLow[7];
            constants.coefLow8 = aces.coefsLow[8];
            constants.coefLow9 = aces.coefsLow[9];

            constants.coefHigh0 = aces.coefsHigh[0];
            constants.coefHigh1 = aces.coefsHigh[1];
            constants.coefHigh2 = aces.coefsHigh[2];
            constants.coefHigh3 = aces.coefsHigh[3];
            constants.coefHigh4 = aces.coefsHigh[4];
            constants.coefHigh5 = aces.coefsHigh[5];
            constants.coefHigh6 = aces.coefsHigh[6];
            constants.coefHigh7 = aces.coefsHigh[7];
            constants.coefHigh8 = aces.coefsHigh[8];
            constants.coefHigh9 = aces.coefsHigh[9];

            return constants;
        }

        class PassData
        {
            public Texture2D grainTexture;
            public float bloomStrength, isSceneView, noiseIntensity, noiseResponse, shutterSpeed, aperture;
            public Vector4 grainTextureParams;
            internal Vector4 resolution;
        }

        struct AcesConstants
        {
            public Vector2 ACES_min;
            public Vector2 ACES_mid;
            public Vector2 ACES_max;
            public Vector2 ACES_slope;

            public Vector2 CinemaLimits;
            public Vector2 Padding;

            // No array support..
            public float coefLow0;
            public float coefLow1;
            public float coefLow2;
            public float coefLow3;
            public float coefLow4;
            public float coefLow5;
            public float coefLow6;
            public float coefLow7;
            public float coefLow8;
            public float coefLow9;

            public float coefHigh0;
            public float coefHigh1;
            public float coefHigh2;
            public float coefHigh3;
            public float coefHigh4;
            public float coefHigh5;
            public float coefHigh6;
            public float coefHigh7;
            public float coefHigh8;
            public float coefHigh9;
        };

    }
}
