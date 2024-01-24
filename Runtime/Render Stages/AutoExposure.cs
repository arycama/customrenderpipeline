using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class AutoExposure : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [SerializeField] private float minEv = -10f;
            [SerializeField] private float maxEv = 18f;
            [SerializeField] private float adaptationSpeed = 1.1f;
            [SerializeField] private float exposureCompensation = 0.0f;
            [SerializeField] private Vector2 histogramPercentages = new(40f, 90f);
            [SerializeField] private int exposureResolution = 128;
            [SerializeField] private AnimationCurve exposureCurve = AnimationCurve.Linear(-10f, -10f, 20f, 20f);

            public float MinEv => minEv;
            public float MaxEv => maxEv;
            public float AdaptationSpeed => adaptationSpeed;
            public float ExposureCompensation => exposureCompensation;
            public AnimationCurve ExposureCurve => exposureCurve;
            public Vector2 HistogramPercentages => histogramPercentages;
            public int ExposureResolution => exposureResolution;
        }

        private readonly Settings settings;
        private readonly LensSettings lensSettings;
        private readonly ComputeShader computeShader;
        private readonly GraphicsBuffer exposureBuffer;

        private readonly Texture2D exposureTexture;

        public AutoExposure(Settings settings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            this.lensSettings = lensSettings;
            computeShader = Resources.Load<ComputeShader>("PostProcessing/AutoExposure");

            exposureBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, 4, sizeof(float));

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

        public void Render(RTHandle input, int width, int height)
        {
            var exposurePixels = ArrayPool<float>.Get(settings.ExposureResolution);
            for (var i = 0; i < settings.ExposureResolution; i++)
            {
                var uv = i / (settings.ExposureResolution - 1f);
                var t = Mathf.Lerp(settings.MinEv, settings.MaxEv, uv);
                var exposure = settings.ExposureCurve.Evaluate(t);
                exposurePixels[i] = exposure;
            }
            exposureTexture.SetPixelData(exposurePixels, 0);
            ArrayPool<float>.Release(exposurePixels);
            exposureTexture.Apply(false, false);

            var histogram = renderGraph.GetBuffer(256);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>())
            {
                pass.Initialize(computeShader, 0, width, height);
                pass.ReadTexture("Input", input);
                pass.WriteBuffer("LuminanceHistogram", histogram);

                pass.SetRenderFunction((command, context) =>
                {
                    pass.SetFloat(command, "MinEv", settings.MinEv);
                    pass.SetFloat(command, "MaxEv", settings.MaxEv);
                    pass.SetFloat(command, "AdaptationSpeed", settings.AdaptationSpeed);
                    pass.SetFloat(command, "ExposureCompensation", settings.ExposureCompensation);
                    pass.SetFloat(command, "Iso", lensSettings.Iso);
                    pass.SetFloat(command, "Aperture", lensSettings.Aperture);
                    pass.SetFloat(command, "ShutterSpeed", lensSettings.ShutterSpeed);
                    pass.SetFloat(command, "HistogramMin", settings.HistogramPercentages.x);
                    pass.SetFloat(command, "HistogramMax", settings.HistogramPercentages.y);
                    pass.SetVector(command, "_ExposureCompensationRemap", GraphicsUtilities.HalfTexelRemap(settings.ExposureResolution, 1));
                });
            }

            var output = renderGraph.GetBuffer(4, target: GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>())
            {
                pass.Initialize(computeShader, 1, 1);
                pass.ReadBuffer("LuminanceHistogram", histogram);
                pass.WriteBuffer("LuminanceOutput", output);

                pass.SetRenderFunction((command, context) =>
                {
                    pass.SetTexture(command, "ExposureTexture", exposureTexture);
                });
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>())
            {
                pass.SetRenderFunction((command, context) =>
                {
                    command.CopyBuffer(output, exposureBuffer);
                    command.SetGlobalConstantBuffer(exposureBuffer, "Exposure", 0, sizeof(float) * 4);
                });
            }
        }
    }
}