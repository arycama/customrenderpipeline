using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Arycama.CustomRenderPipeline
{
    public enum HdrSettingsPreset
    {
        Default,
        _1000NitHDR,
        _1000NitHDRSharpened,
        SDR,
        EDRExtreme,
        EDR,
        Custom
    }

    public class Tonemapping : RenderFeature
    {
        private readonly Settings settings;
        private readonly Bloom.Settings bloomSettings;
        private readonly LensSettings lensSettings;
        private readonly Material material;

        [Serializable]
        public class Settings
        {
            [SerializeField, Range(0.0f, 1.0f)] private float noiseIntensity = 0.5f;
            [SerializeField, Range(0.0f, 1.0f)] private float noiseResponse = 0.8f;
            [SerializeField] private Texture2D filmGrainTexture = null;
            [SerializeField] private float toeStrength = 0.5f;
            [SerializeField] private float toeLength = 0.5f;
            [SerializeField] private float shoulderStrength = 2.0f;
            [SerializeField] private float shoulderLength = 0.5f;
            [SerializeField] private float shoulderAngle = 1.0f;

            [field: SerializeField] public bool HdrEnabled { get; private set; } = true;
            [field: SerializeField] public bool AutoDetectValues { get; private set; } = true;
            [field: SerializeField, Min(0)] public int HdrMinNits { get; private set; } = 0;
            [field: SerializeField, Min(0)] public int HdrMaxNits { get; private set; } = 1000;
            [field: SerializeField, Min(0.0f)] public float PaperWhiteNits { get; private set; } = 300.0f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float HueShift { get; private set; } = 0.0f;

            [field: SerializeField] public HdrSettingsPreset HdrSettingsPreset { get; private set; } = HdrSettingsPreset.Custom;
            [field: SerializeField] public AcesSettings AcesSettings { get; private set; } = new();

            public float NoiseIntensity => noiseIntensity;
            public float NoiseResponse => noiseResponse;
            public Texture2D FilmGrainTexture => filmGrainTexture;
            public float ToeStrength => toeStrength;
            public float ToeLength => toeLength;
            public float ShoulderStrength => shoulderStrength;
            public float ShoulderLength => shoulderLength;
            public float ShoulderAngle => shoulderAngle;
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

        class PassData
        {
            public Texture2D grainTexture;
            public float bloomStrength, isSceneView, toeStrength, toeLength, shoulderStrength, shoulderLength, shoulderAngle, noiseIntensity, noiseResponse, shutterSpeed, aperture;
            public Vector4 grainTextureParams;
            internal Vector4 resolution;
        }

        public void Render(RTHandle input, RTHandle bloom, RTHandle uITexture, bool isSceneView, int width, int height)
        {
            using var pass = renderGraph.AddRenderPass<BlitToScreenPass>("Tonemapping");
            pass.Initialize(material);
            pass.ReadTexture("_MainTex", input);
            pass.ReadTexture("_Bloom", bloom);
            pass.ReadTexture("UITexture", uITexture);

            var minNits = settings.AutoDetectValues ? HDROutputSettings.main.minToneMapLuminance : settings.HdrMinNits;
            var maxNits = settings.AutoDetectValues ? HDROutputSettings.main.maxToneMapLuminance : settings.HdrMaxNits;
            if (minNits < 0 || maxNits <= 0)
            {
                minNits = settings.HdrMinNits;
                maxNits = settings.HdrMaxNits;
            }

            var paperWhiteNits = settings.AutoDetectValues ? HDROutputSettings.main.paperWhiteNits : settings.PaperWhiteNits;
            if (paperWhiteNits <= 0)
            {
                paperWhiteNits = settings.PaperWhiteNits;
                HDROutputSettings.main.paperWhiteNits = paperWhiteNits;
            }

            var acesSettingsBuffer = renderGraph.SetConstantBuffer(GetAcesConstants());
            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                pass.ReadBuffer("AcesConstants", acesSettingsBuffer);

                pass.SetFloat(command, "HdrEnabled", HDROutputSettings.main.available ? 1.0f : 0.0f);
                pass.SetTexture(command, "_GrainTexture", data.grainTexture);

                pass.SetFloat(command, "_BloomStrength", data.bloomStrength);
                pass.SetFloat(command, "_IsSceneView", data.isSceneView);
                pass.SetFloat(command, "ToeStrength", data.toeStrength);
                pass.SetFloat(command, "ToeLength", data.toeLength);
                pass.SetFloat(command, "ShoulderStrength", data.shoulderStrength);
                pass.SetFloat(command, "ShoulderLength", data.shoulderLength);
                pass.SetFloat(command, "ShoulderAngle", data.shoulderAngle);
                pass.SetFloat(command, "NoiseIntensity", data.noiseIntensity);
                pass.SetFloat(command, "NoiseResponse", data.noiseResponse);

                pass.SetFloat(command, "PaperWhiteNits", paperWhiteNits);
                pass.SetFloat(command, "HdrMinNits", minNits);
                pass.SetFloat(command, "HdrMaxNits", maxNits);

                pass.SetFloat(command, "ShutterSpeed", data.shutterSpeed);
                pass.SetFloat(command, "Aperture", data.aperture);
                pass.SetVector(command, "_GrainTextureParams", data.grainTextureParams);
                pass.SetVector(command, "_Resolution", data.resolution);

                pass.SetVector(command, "_BloomScaleLimit", new Vector4(bloom.Scale.x, bloom.Scale.y, bloom.Limit.x, bloom.Limit.y));
                pass.SetFloat(command, "HueShift", settings.HueShift);

                var colorGamut = HDROutputSettings.main.displayColorGamut;
                pass.SetInt(command, "ColorGamut", (int)colorGamut);

                var colorPrimaries = ColorGamutUtility.GetColorPrimaries(colorGamut);
                pass.SetInt(command, "ColorPrimaries", (int)colorPrimaries);

                var transferFunction = ColorGamutUtility.GetTransferFunction(colorGamut);
                pass.SetInt(command, "TransferFunction", (int)transferFunction);
            });

            var offsetX = Random.value;
            var offsetY = Random.value;
            var uvScaleX = settings.FilmGrainTexture ? width / (float)settings.FilmGrainTexture.width : 1.0f;
            var uvScaleY = settings.FilmGrainTexture ? height / (float)settings.FilmGrainTexture.height : 1.0f;

            data.grainTexture = settings.FilmGrainTexture;
            data.bloomStrength = bloomSettings.Strength;
            data.isSceneView = isSceneView ? 1.0f : 0.0f;
            data.toeStrength = settings.ToeStrength;
            data.toeLength = settings.ToeLength;
            data.shoulderStrength = settings.ShoulderStrength;
            data.shoulderLength = settings.ShoulderLength;
            data.shoulderAngle = settings.ShoulderAngle;
            data.noiseIntensity = settings.NoiseIntensity;
            data.noiseResponse = settings.NoiseResponse;
            data.shutterSpeed = lensSettings.ShutterSpeed;
            data.aperture = lensSettings.Aperture;
            data.grainTextureParams = new Vector4(uvScaleX, uvScaleY, offsetX, offsetY);
            data.resolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
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
            public Vector4 ACES_coef0;
            public Vector4 ACES_coef1;
            public Vector4 ACES_coef2;
            public Vector4 ACES_coef3;
            public Vector4 ACES_coef4;
            public Vector4 ACES_coef5;
            public Vector4 ACES_coef6;
            public Vector4 ACES_coef7;
            public Vector4 ACES_coef8;
            public Vector4 ACES_coef9;
        };

        public float MaxLevel => settings.AcesSettings.maxLevel;

        AcesConstants GetAcesConstants()
        {
            var preset = this.settings.HdrSettingsPreset;

            //preset = this.settings.HdrEnabled ? HdrSettingsPreset._1000NitHDRSharpened : HdrSettingsPreset.SDR;

            AcesSettings hdrSettings;
            switch(preset)
            {
                case HdrSettingsPreset.Default:
                    hdrSettings = new(ODTCurve.ODT_LDR_Ref, 0, 8, -1, 1);
                    break;
                case HdrSettingsPreset._1000NitHDR:
                    hdrSettings = new AcesSettings(ODTCurve.ODT_1000Nit_Adj, -12, 10, MaxLevel, 1);
                    break;
                case HdrSettingsPreset._1000NitHDRSharpened:
                    hdrSettings = new AcesSettings(ODTCurve.ODT_1000Nit_Adj, -8, 8, MaxLevel, 1);
                    break;
                case HdrSettingsPreset.SDR:
                    hdrSettings = new AcesSettings(ODTCurve.ODT_LDR_Adj, -6.5f, 6.5f, -1, 1);
                    break;
                case HdrSettingsPreset.EDRExtreme:
                    hdrSettings = new AcesSettings(ODTCurve.ODT_1000Nit_Adj, -12, 9, MaxLevel, 1);
                    break;
                case HdrSettingsPreset.EDR:
                    hdrSettings = new AcesSettings(ODTCurve.ODT_1000Nit_Adj, -8, 8, MaxLevel, 3);
                    break;
                case HdrSettingsPreset.Custom:
                    hdrSettings = this.settings.AcesSettings;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(this.settings.HdrSettingsPreset));
            }

            AcesConstants constants;

            // setup the aces data
            var aces = Aces.GetAcesODTData(hdrSettings.ToneCurve, hdrSettings.minStops, hdrSettings.maxStops, hdrSettings.maxLevel, hdrSettings.midGrayScale);

            constants.ACES_coef0 = aces.coefs[0];
            constants.ACES_coef1 = aces.coefs[1];
            constants.ACES_coef2 = aces.coefs[2];
            constants.ACES_coef3 = aces.coefs[3];
            constants.ACES_coef4 = aces.coefs[4];
            constants.ACES_coef5 = aces.coefs[5];
            constants.ACES_coef6 = aces.coefs[6];
            constants.ACES_coef7 = aces.coefs[7];
            constants.ACES_coef8 = aces.coefs[8];
            constants.ACES_coef9 = aces.coefs[9];
            constants.ACES_max = aces.maxPoint;
            constants.ACES_mid = aces.midPoint;
            constants.ACES_min = aces.minPoint;
            constants.ACES_slope = aces.slope;

            constants.CinemaLimits.x = aces.minPoint.y;
            constants.CinemaLimits.y = aces.maxPoint.y;
            constants.Padding = Vector2.zero;

            return constants;
        }
    }

    [Serializable]
    public class AcesSettings
    {
        public ODTCurve ToneCurve = ODTCurve.ODT_LDR_Ref; // Tone Curve
        [Range(-14.0f, 0.0f)] public float minStops = 0.0f;
        [Range(0.0f, 20.0f)] public float maxStops = 8.0f;
        [Range(-1.0f, 4000.0f)] public float maxLevel = -1.0f; // "Max Level (nits -1=default)"
        [Range(0.01f, 100.0f)] public float midGrayScale = 1.0f; // "Middle Gray Scale"

        public AcesSettings()
        {
            ToneCurve = ODTCurve.ODT_LDR_Ref;
            maxStops = 8.0f;
            maxLevel = -1.0f;
            midGrayScale = 1.0f;
        }

        public AcesSettings(ODTCurve toneCurve, float minStops, float maxStops, float maxLevel, float midGrayScale)
        {
            ToneCurve = toneCurve;
            this.minStops = minStops;
            this.maxStops = maxStops;
            this.maxLevel = maxLevel;
            this.midGrayScale = midGrayScale;
        }
    };
}
