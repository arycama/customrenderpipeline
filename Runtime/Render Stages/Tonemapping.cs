using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Random = UnityEngine.Random;

namespace Arycama.CustomRenderPipeline
{
    public partial class Tonemapping : RenderFeature<(int width, int height)>
    {
        private readonly Settings settings;
        private readonly Bloom.Settings bloomSettings;
        private readonly LensSettings lensSettings;
        private readonly Material material;

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

        public override void Render((int width, int height) data)
        {
            var result = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.B10G11R11_UFloatPack32);

            using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Tonemapping");
            pass.Initialize(material);
            pass.WriteTexture(result);
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();
            pass.AddRenderPassData<CameraTargetData>();
            pass.AddRenderPassData<BloomData>();

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

            pass.SetRenderFunction((command, pass) =>
            {
                var offsetX = Random.value;
                var offsetY = Random.value;
                var uvScaleX = settings.FilmGrainTexture ? data.width / (float)settings.FilmGrainTexture.width : 1.0f;
                var uvScaleY = settings.FilmGrainTexture ? data.height / (float)settings.FilmGrainTexture.height : 1.0f;

                var hdrEnabled = hdrSettings.available && settings.HdrEnabled;
                var maxNits = hdrEnabled ? hdrSettings.maxToneMapLuminance : 100.0f;

                pass.SetFloat(command, "HdrEnabled", hdrEnabled ? 1.0f : 0.0f);
                pass.SetTexture(command, "_GrainTexture", settings.FilmGrainTexture);

                pass.SetFloat(command, "NoiseIntensity", settings.NoiseIntensity);
                pass.SetFloat(command, "NoiseResponse", settings.NoiseResponse);

                pass.SetFloat(command, "Tonemap", settings.Tonemap ? 1.0f : 0.0f);

                pass.SetFloat(command, "MaxLuminance", hdrEnabled ? maxNits : 100);
                pass.SetFloat(command, "PaperWhiteLuminance", settings.PaperWhite); // Todo: Brightness setting
                pass.SetFloat(command, "PaperWhiteBoost", settings.GreyLuminanceBoost);
                pass.SetFloat(command, "Contrast", settings.Contrast);
                pass.SetFloat(command, "Toe", settings.Toe);
                pass.SetFloat(command, "PurityCompress", settings.PurityCompress);
                pass.SetFloat(command, "PurityBoost", settings.PurityBoost);
                pass.SetVector(command, "Hueshift", new Vector3(settings.HueshiftR, settings.HueshiftG, settings.HueshiftB));

                pass.SetFloat(command, "ShutterSpeed", lensSettings.ShutterSpeed);
                pass.SetFloat(command, "Aperture", lensSettings.Aperture);
                pass.SetVector(command, "_GrainTextureParams", new Vector4(uvScaleX, uvScaleY, offsetX, offsetY));
                pass.SetVector(command, "_Resolution", new Vector4(data.width, data.height, 1.0f / data.width, 1.0f / data.height));

                var colorGamut = hdrSettings.available ? hdrSettings.displayColorGamut : ColorGamut.sRGB;
                pass.SetInt(command, "ColorGamut", (int)colorGamut);

                pass.SetFloat(command, "_BloomStrength", bloomSettings.Strength);
            });

            renderGraph.SetResource(new CameraTargetData(result));;
        }
    }
}
