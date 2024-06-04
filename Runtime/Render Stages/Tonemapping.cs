using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

        static Matrix3x4[] ColorMatrices = new Matrix3x4[3]
        {
	        // rec 709
	        new (3.24096942f, -1.53738296f, -0.49861076f, 0.0f,
            -0.96924388f, 1.87596786f, 0.04155510f, 0.0f,
            0.05563002f, -0.20397684f, 1.05697131f, 0.0f),

	        // DCI-P3
	        new (2.72539496f, -1.01800334f, -0.44016343f, 0.0f,
            -0.79516816f, 1.68973231f, 0.02264720f, 0.0f,
            0.04124193f, -0.08763910f, 1.10092998f, 0.0f),

	        // BT2020
	        new(1.71665096f, -0.35567081f, -0.25336623f, 0.0f,
            -0.66668433f, 1.61648130f, 0.01576854f, 0.0f,
            0.01763985f, -0.04277061f, 0.94210327f, 0.0f)
        };

        static Matrix3x4[] ColorMatricesInv = new Matrix3x4[3]
        {
	        //rec709 to XYZ
	        new (0.41239089f, 0.35758430f, 0.18048084f, 0.0f,
            0.21263906f, 0.71516860f, 0.07219233f, 0.0f,
            0.01933082f, 0.11919472f, 0.95053232f, 0.0f),

	        //DCI - P3 2 XYZ
	        new (0.44516969f, 0.27713439f, 0.17228261f, 0.0f,
            0.20949161f, 0.72159523f, 0.06891304f, 0.0f,
            0.00000000f, 0.04706058f, 0.90735501f, 0.0f),

	        //bt2020 2 XYZ
	        new (0.63695812f, 0.14461692f, 0.16888094f, 0.0f,
            0.26270023f, 0.67799807f, 0.05930171f, 0.0f,
            0.00000000f, 0.02807269f, 1.06098485f, 0.0f)
        };

        struct AcesConstants
        {
            public Vector2 ACES_min;
            public Vector2 ACES_mid;
            public Vector2 ACES_max;
            public Vector2 ACES_slope;

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

            public Matrix3x4 colorMat;
            public Matrix3x4 colorMatInv;
            public Vector2 CinemaLimits;
            public int OutputMode;
            public int flags;
            public float surroundGamma;
            public float saturation;
            public float postScale;
            public float gamma;
        };

        AcesConstants GetAcesConstants()
        {
            var settings = this.settings.AcesSettings;
            AcesConstants constants;

            // setup the color matrix
            constants.colorMat = ColorMatrices[(int)settings.ColorSpace];
            constants.colorMatInv = ColorMatricesInv[(int)settings.ColorSpace];

            // setup the aces data
            var aces = Aces.GetAcesODTData(settings.ToneCurve, settings.minStops, settings.maxStops, settings.maxLevel, settings.midGrayScale);

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

            constants.flags = settings.adjustWP ? 0x4 : 0x0;
            constants.flags |= settings.desaturate ? 0x2 : 0x0;
            constants.flags |= settings.dimSurround ? 0x1 : 0x0;
            constants.flags |= settings.luminanceOnly ? 0x8 : 0x0;

            constants.OutputMode = (int)settings.EOTF;
            constants.saturation = settings.toneCurveSaturation;
            constants.surroundGamma = settings.surroundGamma;
            constants.gamma = settings.outputGamma;
            constants.postScale = 1.0f;

            return constants;
        }
    }
}