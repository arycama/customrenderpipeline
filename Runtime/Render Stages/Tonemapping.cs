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

                pass.SetFloat("HdrEnabled", hdrEnabled ? 1.0f : 0.0f);
                pass.SetTexture("_GrainTexture", settings.FilmGrainTexture);

                pass.SetFloat("NoiseIntensity", settings.NoiseIntensity);
                pass.SetFloat("NoiseResponse", settings.NoiseResponse);

                pass.SetFloat("Tonemap", settings.Tonemap ? 1.0f : 0.0f);

                pass.SetFloat("MaxLuminance", hdrEnabled ? maxNits : 100);
                pass.SetFloat("PaperWhiteLuminance", settings.PaperWhite); // Todo: Brightness setting
                pass.SetFloat("PaperWhiteBoost", settings.GreyLuminanceBoost);
                pass.SetFloat("Contrast", settings.Contrast);
                pass.SetFloat("Toe", settings.Toe);
                pass.SetFloat("PurityCompress", settings.PurityCompress);
                pass.SetFloat("PurityBoost", settings.PurityBoost);
                pass.SetVector("Hueshift", new Vector3(settings.HueshiftR, settings.HueshiftG, settings.HueshiftB));

                pass.SetFloat("ShutterSpeed", lensSettings.ShutterSpeed);
                pass.SetFloat("Aperture", lensSettings.Aperture);
                pass.SetVector("_GrainTextureParams", new Vector4(uvScaleX, uvScaleY, offsetX, offsetY));
                pass.SetVector("_Resolution", new Vector4(data.width, data.height, 1.0f / data.width, 1.0f / data.height));

                var colorGamut = hdrSettings.available ? hdrSettings.displayColorGamut : ColorGamut.sRGB;
                pass.SetInt("ColorGamut", (int)colorGamut);

                pass.SetFloat("_BloomStrength", bloomSettings.Strength);
            });

            renderGraph.SetResource(new CameraTargetData(result));;
        }
    }
}
