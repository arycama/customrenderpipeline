using UnityEngine;
using Object = UnityEngine.Object;

namespace Arycama.CustomRenderPipeline
{
    public partial class AutoExposure : RenderFeature
    {
        private readonly Settings settings;
        private readonly LensSettings lensSettings;
        private readonly ComputeShader computeShader;
        private readonly Texture2D exposureTexture;

        public AutoExposure(Settings settings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            this.lensSettings = lensSettings;
            computeShader = Resources.Load<ComputeShader>("PostProcessing/AutoExposure");


            var exposurePixels = ArrayPool<float>.Get(settings.ExposureResolution);
            for (var i = 0; i < settings.ExposureResolution; i++)
            {
                var uv = i / (settings.ExposureResolution - 1f);
                var t = Mathf.Lerp(settings.MinEv, settings.MaxEv, uv);
                var exposure = settings.ExposureCurve.Evaluate(t);
                exposurePixels[i] = exposure;
            }

            exposureTexture = new Texture2D(settings.ExposureResolution, 1, TextureFormat.RFloat, false) { hideFlags = HideFlags.HideAndDontSave };
            exposureTexture.SetPixelData(exposurePixels, 0);
            ArrayPool<float>.Release(exposurePixels);
            exposureTexture.Apply(false, false);
        }

        protected override void Cleanup(bool disposing)
        {
            Object.DestroyImmediate(exposureTexture);
        }

        public override void Render()
        {
            var exposurePixels = exposureTexture.GetRawTextureData<float>();
            for (var i = 0; i < settings.ExposureResolution; i++)
            {
                var uv = i / (settings.ExposureResolution - 1f);
                var t = Mathf.Lerp(settings.MinEv, settings.MaxEv, uv);
                var exposure = settings.ExposureCurve.Evaluate(t);
                exposurePixels[i] = exposure;
            }

            exposureTexture.SetPixelData(exposurePixels, 0);
            exposureTexture.Apply(false, false);

            var histogram = renderGraph.GetBuffer(256);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Auto Exposure"))
            {
                var viewData = renderGraph.GetResource<ViewData>();
                pass.Initialize(computeShader, 0, viewData.ScaledWidth, viewData.ScaledHeight);
                pass.ReadTexture("Input", renderGraph.GetResource<CameraTargetData>().Handle);
                pass.WriteBuffer("LuminanceHistogram", histogram);
                pass.AddRenderPassData<AutoExposureData>();

                pass.SetRenderFunction(
                (
                    minEv: settings.MinEv,
                    maxEv: settings.MaxEv,
                    adaptationSpeed: settings.AdaptationSpeed,
                    exposureCompensation: settings.ExposureCompensation,
                    iso: lensSettings.Iso,
                    aperture: lensSettings.Aperture,
                    shutterSpeed: lensSettings.ShutterSpeed,
                    histogramMin: settings.HistogramMin,
                    histogramMax: settings.HistogramMax,
                    exposureCompensationRemap: GraphicsUtilities.HalfTexelRemap(settings.ExposureResolution, 1)
                ),

                (command, pass, data) =>
                {
                    pass.SetFloat("MinEv", data.minEv);
                    pass.SetFloat("MaxEv", data.maxEv);
                    pass.SetFloat("AdaptationSpeed", data.adaptationSpeed);
                    pass.SetFloat("ExposureCompensation", data.exposureCompensation);
                    pass.SetFloat("Iso", data.iso);
                    pass.SetFloat("Aperture", data.aperture);
                    pass.SetFloat("ShutterSpeed", data.shutterSpeed);
                    pass.SetFloat("HistogramMin", data.histogramMin);
                    pass.SetFloat("HistogramMax", data.histogramMax);
                    pass.SetFloat("MeteringMode", (float)settings.MeteringMode);
                    pass.SetVector("_ExposureCompensationRemap", data.exposureCompensationRemap);
                    pass.SetVector("ProceduralCenter", settings.ProceduralCenter);
                    pass.SetVector("ProceduralRadii", settings.ProceduralRadii);
                    pass.SetFloat("ProceduralSoftness", settings.ProceduralSoftness);
                });
            }

            var output = renderGraph.GetBuffer(1, 16, target: GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Auto Exposure"))
            {
                pass.Initialize(computeShader, 1, 1);
                pass.ReadBuffer("LuminanceHistogram", histogram);
                pass.WriteBuffer("LuminanceOutput", output);
                pass.AddRenderPassData<AutoExposureData>();

                pass.SetRenderFunction((settings.ExposureMode, exposureTexture), (command, pass, data) =>
                {
                    pass.SetFloat("Mode", (float)data.ExposureMode);
                    pass.SetFloat("IsFirst", 0.0f);
                    pass.SetTexture("ExposureTexture", data.exposureTexture);
                });
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Auto Exposure"))
            {
                pass.SetRenderFunction(output, (command, pass, data) =>
                {
                    var exposureData = pass.RenderGraph.GetResource<AutoExposureData>();
                    command.CopyBuffer(data, exposureData.exposureBuffer);
                });
            }
        }
    }
}