using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public partial class TemporalAA : RenderFeature
    {
        private readonly Settings settings;
        private readonly PersistentRTHandleCache textureCache;
        private readonly Material material;

        public TemporalAA(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Temporal AA")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Temporal AA");
        }

        protected override void Cleanup(bool disposing)
        {
            textureCache.Dispose();
        }

        public void OnPreRender(int scaledWidth, int scaledHeight, out Vector2 jitter)
        {
            var sampleIndex = renderGraph.FrameIndex % settings.SampleCount + 1;

            jitter.x = Halton(sampleIndex, 2) - 0.5f;
            jitter.y = Halton(sampleIndex, 3) - 0.5f;

            jitter *= settings.JitterSpread;

            var previousSampleIndex = Math.Max(0, renderGraph.FrameIndex - 1) % settings.SampleCount + 1;

            Vector2 previousJitter;
            previousJitter.x = Halton(previousSampleIndex, 2) - 0.5f;
            previousJitter.y = Halton(previousSampleIndex, 3) - 0.5f;

            previousJitter *= settings.JitterSpread;

            if (settings.JitterOverride)
                jitter = settings.JitterOverrideValue;

            if (!settings.IsEnabled)
                jitter = previousJitter = Vector2.zero;

            var weights = ArrayPool<float>.Get(9);
            float boxWeightSum = 0.0f, crossWeightSum = 0.0f;
            float maxCrossWeight = 0.0f, maxBoxWeight = 0.0f;
            for (int y = -1, i = 0; y <= 1; y++)
            {
                for (var x = -1; x <= 1; x++, i++)
                {
                    var weight = Mitchell(x + jitter.x, y + jitter.y);

                    if (!settings.IsEnabled)
                        weight = (x == 0 && y == 0) ? 1.0f : 0.0f;

                    weights[i] = weight;
                    boxWeightSum += weight;
                    maxBoxWeight = Mathf.Max(maxBoxWeight, weight);

                    if (x == 0 || y == 0)
                    {
                        crossWeightSum += weight;
                        maxCrossWeight = Mathf.Max(maxCrossWeight, weight);
                    }
                }
            }

            // Normalize weights
            var rcpCrossWeightSum = 1.0f / crossWeightSum;
            var rcpBoxWeightSum = 1.0f / boxWeightSum;

            renderGraph.SetResource(new TemporalAAData
            (
                renderGraph.SetConstantBuffer((
                    new Vector4(jitter.x, jitter.y, jitter.x / scaledWidth, jitter.y / scaledHeight),
                    new Vector4(previousJitter.x, previousJitter.y, previousJitter.x / scaledWidth, previousJitter.y / scaledHeight),
                    crossWeightSum,
                    boxWeightSum,
                    weights[4] * rcpCrossWeightSum,
                    weights[4] * rcpBoxWeightSum,
                    new Vector4(weights[1], weights[3], weights[5], weights[7]) * rcpCrossWeightSum,
                    new Vector4(weights[0], weights[1], weights[2], weights[3]) * rcpBoxWeightSum,
                    new Vector4(weights[5], weights[6], weights[7], weights[8]) * rcpBoxWeightSum))
            )); ;

            ArrayPool<float>.Release(weights);
        }

        public override void Render()
        {
            if (!settings.IsEnabled)
                return;

            var viewData = renderGraph.GetResource<ViewData>();
            var (current, history, wasCreated) = textureCache.GetTextures(viewData.PixelWidth, viewData.PixelHeight, viewData.ViewIndex);
            var result = renderGraph.GetTexture(viewData.PixelWidth, viewData.PixelHeight, GraphicsFormat.B10G11R11_UFloatPack32);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal AA"))
            {
                var keyword = viewData.Scale < 1.0f ? "UPSCALE" : null;
                pass.Initialize(material, 0, 1, keyword);

                pass.ReadTexture("_History", history);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<CameraTargetData>();
                pass.AddRenderPassData<VelocityData>();

                pass.SetRenderFunction((
                    spatialSharpness: settings.SpatialSharpness,
                    motionSharpness: settings.MotionSharpness * 0.8f,
                    hasHistory: wasCreated ? 0.0f : 1.0f,
                    stationaryBlending: settings.StationaryBlending,
                    motionBlending: settings.MotionBlending,
                    motionWeight: settings.MotionWeight,
                    scale: viewData.Scale,
                    maxWidth: viewData.ScaledWidth - 1,
                    maxHeight: viewData.ScaledHeight - 1,
                    maxResolution: new Vector2(viewData.PixelWidth - 1, viewData.PixelHeight - 1)
                ),
                (command, pass, data) =>
                {
                    pass.SetFloat("_SpatialSharpness", data.spatialSharpness);
                    pass.SetFloat("_MotionSharpness", data.motionSharpness);
                    pass.SetFloat("_HasHistory", data.hasHistory);
                    pass.SetFloat("_StationaryBlending", data.stationaryBlending);
                    pass.SetFloat("_VelocityBlending", data.motionBlending);
                    pass.SetFloat("_VelocityWeight", data.motionWeight);
                    pass.SetFloat("_Scale", data.scale);

                    pass.SetVector("_HistoryScaleLimit", new Vector4(history.Scale.x, history.Scale.y, history.Limit.x, history.Limit.y));

                    pass.SetVector("_MaxResolution", data.maxResolution);

                    pass.SetInt("_MaxWidth", data.maxWidth);
                    pass.SetInt("_MaxHeight", data.maxHeight);
                });

                renderGraph.SetResource(new CameraTargetData(result)); ;
            }
        }

        public static float Halton(int index, int radix)
        {
            var result = 0f;
            var fraction = 1f / radix;

            while (index > 0)
            {
                result += index % radix * fraction;

                index /= radix;
                fraction /= radix;
            }

            return result;
        }

        private float Mitchell1D(float x)
        {
            var B = 0.0f;
            var C = settings.SpatialSharpness;
            x = Mathf.Abs(x) * (4.0f / 3.0f);

            if (x <= 1.0f)
                return ((12 - 9 * B - 6 * C) * x * x * x + (-18 + 12 * B + 6 * C) * x * x + (6 - 2 * B)) * (1.0f / 6.0f);
            else if (x <= 2.0f)
                return ((-B - 6 * C) * x * x * x + (6 * B + 30 * C) * x * x + (-12 * B - 48 * C) * x + (8 * B + 24 * C)) * (1.0f / 6.0f);
            else
                return 0.0f;
        }

        private float Mitchell(float x, float y)
        {
            return Mitchell1D(x) * Mitchell1D(y);
        }
    }
}