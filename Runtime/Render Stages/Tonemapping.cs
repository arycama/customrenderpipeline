﻿using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
            [field: Header("Tonemapping")]
            [field: SerializeField] public bool Tonemap { get; private set; } = true;
            [field: SerializeField, Range(0.0f, 1.0f)] public float ToeStrength { get; private set; } = 0.0f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float ToeLength { get; private set; } = 0.5f;
            [field: SerializeField, Min(0.0f)] public float ShoulderStrength { get; private set; } = 0.0f;
            [field: SerializeField, Range(1e-5f, 1.0f)] public float ShoulderLength { get; private set; } = 0.5f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float ShoulderAngle { get; private set; } = 0.0f;
            [field: SerializeField, Min(0.0f)] public float Gamma { get; private set; } = 1.0f;

            [field: Header("Hdr Output")]
            [field: SerializeField] public bool HdrEnabled { get; private set; } = true;
            [field: SerializeField] public bool AutoDetectValues { get; private set; } = true;
            [field: SerializeField, Min(0)] public float HdrMinNits { get; private set; } = 0;
            [field: SerializeField, Min(0)] public float HdrMaxNits { get; private set; } = 1000;
            [field: SerializeField, Min(0.0f)] public float PaperWhiteNits { get; private set; } = 300.0f;
            [field: SerializeField] public HDRDisplayBitDepth BitDepth { get; private set; } = HDRDisplayBitDepth.BitDepth10;

            // TODO: Move to lens settings or something?
            [field: Header("Film Grain")]
            [field: SerializeField, Range(0.0f, 1.0f)] public float NoiseIntensity { get; private set; } = 0.5f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float NoiseResponse { get; private set; } = 0.8f;
            [field: SerializeField] public Texture2D FilmGrainTexture { get; private set; } = null;
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

        public RTHandle Render(RTHandle input, RTHandle bloom, int width, int height)
        {
            var result = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32);

            using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Tonemapping");
            pass.Initialize(material);
            pass.WriteTexture(result);
            pass.ReadTexture("_MainTex", input);
            pass.ReadTexture("_Bloom", bloom);
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();

#if UNITY_EDITOR
            PlayerSettings.hdrBitDepth = settings.BitDepth;
#endif

            var hdrSettings = HDROutputSettings.main;
            //var minNits = hdrSettings.available && settings.AutoDetectValues ? hdrSettings.minToneMapLuminance : settings.HdrMinNits;
            //var maxNits = hdrSettings.available && settings.AutoDetectValues ? hdrSettings.maxToneMapLuminance : settings.HdrMaxNits;
            //if (minNits < 0 || maxNits <= 0)
            //{
            //    minNits = settings.HdrMinNits;
            //    maxNits = settings.HdrMaxNits;
            //}

            var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
            {
                var offsetX = Random.value;
                var offsetY = Random.value;
                var uvScaleX = settings.FilmGrainTexture ? width / (float)settings.FilmGrainTexture.width : 1.0f;
                var uvScaleY = settings.FilmGrainTexture ? height / (float)settings.FilmGrainTexture.height : 1.0f;

                pass.SetFloat(command, "HdrEnabled", hdrSettings.available && settings.HdrEnabled ? 1.0f : 0.0f);
                pass.SetTexture(command, "_GrainTexture", settings.FilmGrainTexture);

                pass.SetFloat(command, "NoiseIntensity", settings.NoiseIntensity);
                pass.SetFloat(command, "NoiseResponse", settings.NoiseResponse);

                pass.SetFloat(command, "PaperWhiteNits", settings.PaperWhiteNits);
                pass.SetFloat(command, "HdrMinNits", settings.HdrMinNits);
                pass.SetFloat(command, "HdrMaxNits", settings.HdrMaxNits);
                pass.SetFloat(command, "Tonemap", settings.Tonemap ? 1.0f : 0.0f);

                pass.SetFloat(command, "ToeStrength", settings.ToeStrength);
                pass.SetFloat(command, "ToeLength", Mathf.Pow(settings.ToeLength, 2.2f));
                pass.SetFloat(command, "ShoulderStrength", settings.ShoulderStrength);
                pass.SetFloat(command, "ShoulderLength", settings.ShoulderLength);
                pass.SetFloat(command, "ShoulderAngle", settings.ShoulderAngle);
                pass.SetFloat(command, "Gamma", settings.Gamma);

                pass.SetFloat(command, "ShutterSpeed", lensSettings.ShutterSpeed);
                pass.SetFloat(command, "Aperture", lensSettings.Aperture);
                pass.SetVector(command, "_GrainTextureParams", new Vector4(uvScaleX, uvScaleY, offsetX, offsetY));
                pass.SetVector(command, "_Resolution", new Vector4(width, height, 1.0f / width, 1.0f / height));

                pass.SetVector(command, "_BloomScaleLimit", new Vector4(bloom.Scale.x, bloom.Scale.y, bloom.Limit.x, bloom.Limit.y));

                var colorGamut = hdrSettings.available ? hdrSettings.displayColorGamut : ColorGamut.sRGB;
                pass.SetInt(command, "ColorGamut", (int)colorGamut);

                pass.SetFloat(command, "_BloomStrength", bloomSettings.Strength);
            });

            return result;
        }
    }
}
