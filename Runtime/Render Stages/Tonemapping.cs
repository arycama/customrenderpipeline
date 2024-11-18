using System;
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

            [field: Header("Tonescale Parameters")]
            [field: SerializeField, Range(80, 480)] public float PaperWhite { get; private set; } = 203;
            [field: SerializeField, Range(0, 0.5f)] public float GreyLuminanceBoost { get; private set; } = 0.12f;
            [field: SerializeField, Range(1.0f, 2.0f)] public float Contrast { get; private set; } = 1.4f;
            [field: SerializeField, Range(0, 0.02f)] public float Toe { get; private set; } = 0.001f;

            [field: Header("Color Parameters")]
            [field: SerializeField, Range(0, 1)] public float PurityCompress { get; private set; } = 0.3f;
            [field: SerializeField, Range(0, 1)] public float PurityBoost { get; private set; } = 0.3f;
            [field: SerializeField, Range(-1, 1)] public float HueshiftR { get; private set; } = 0.3f;
            [field: SerializeField, Range(-1, 1)] public float HueshiftG { get; private set; } = 0;
            [field: SerializeField, Range(-1, 1)] public float HueshiftB { get; private set; } = -0.3f;

            [field: Header("Hdr Output")]
            [field: SerializeField] public bool HdrEnabled { get; private set; } = true;
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

        private const float ReflectedLightMeterConstant = 12.5f;
        private const float Sensitivity = 100.0f;

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

                var hdrEnabled = hdrSettings.available && settings.HdrEnabled;
                var maxNits = hdrEnabled ? hdrSettings.maxToneMapLuminance : 100.0f;

                pass.SetFloat(command, "HdrEnabled", hdrEnabled ? 1.0f : 0.0f);
                pass.SetTexture(command, "_GrainTexture", settings.FilmGrainTexture);

                pass.SetFloat(command, "NoiseIntensity", settings.NoiseIntensity);
                pass.SetFloat(command, "NoiseResponse", settings.NoiseResponse);

                pass.SetFloat(command, "Tonemap", settings.Tonemap ? 1.0f : 0.0f);

                pass.SetFloat(command, "MaxLuminance", hdrEnabled ? maxNits : 100);
                pass.SetFloat(command, "PaperWhiteLuminance", hdrEnabled ? settings.PaperWhite : 100.0f); // Todo: Brightness setting
                pass.SetFloat(command, "PaperWhiteBoost", settings.GreyLuminanceBoost);
                pass.SetFloat(command, "Contrast", settings.Contrast);
                pass.SetFloat(command, "Toe", settings.Toe);
                pass.SetFloat(command, "PurityCompress", settings.PurityCompress);
                pass.SetFloat(command, "PurityBoost", settings.PurityBoost);
                pass.SetVector(command, "Hueshift", new Vector3(settings.HueshiftR, settings.HueshiftG, settings.HueshiftB));

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
