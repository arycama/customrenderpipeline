using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class TemporalAA : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField] public bool IsEnabled { get; private set; } = true;
            [field: SerializeField, Range(0.0f, 1.0f)] public float JitterSpread { get; private set; } = 1.0f;
            [field: SerializeField, Range(0f, 2f)] public float SpatialSharpness { get; private set; } = 0.5f;
            [field: SerializeField, Range(0f, 1f)] public float MotionSharpness { get; private set; } = 0.5f;
            [field: SerializeField, Range(0f, 0.99f)] public float StationaryBlending { get; private set; } = 0.95f;
            [field: SerializeField, Range(0f, 0.99f)] public float MotionBlending { get; private set; } = 0.85f;
            [field: SerializeField, Range(1, 32)] public int SampleCount { get; private set; } = 8;
            [field: SerializeField] public float MotionWeight { get; private set; } = 6000f;
            [field: SerializeField] public bool JitterOverride { get; private set; } = false;
            [field: SerializeField] public Vector2 JitterOverrideValue { get; private set; } = Vector2.zero;
        }

        private readonly Settings settings;
        private readonly PersistentRTHandleCache textureCache;
        private readonly Material material;

        public TemporalAA(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Temporal AA")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Temporal AA");
        }

        public readonly struct TemporalAAData : IRenderPassData
        {
            private readonly Vector4 jitter;
            private readonly Vector4 previousJitter;
            private readonly float crossWeightSum;
            private readonly float boxWeightSum;
            private readonly float centerCrossFilterWeight;
            private readonly float centerBoxFilterWeight;
            private readonly Vector4 crossFilterWeights;
            private readonly Vector4 boxFilterWeights0;
            private readonly Vector4 boxFilterWeights1;

            public readonly Vector4 Jitter => jitter;

            public TemporalAAData(Vector4 jitter, Vector4 previousJitter, float crossWeightSum, float boxWeightSum, float centerCrossFilterWeight, float centerBoxFilterWeight, Vector4 crossFilterWeights, Vector4 boxFilterWeights0, Vector4 boxFilterWeights1)
            {
                this.jitter = jitter;
                this.previousJitter = previousJitter;
                this.crossWeightSum = crossWeightSum;
                this.boxWeightSum = boxWeightSum;
                this.centerCrossFilterWeight = centerCrossFilterWeight;
                this.centerBoxFilterWeight = centerBoxFilterWeight;
                this.crossFilterWeights = crossFilterWeights;
                this.boxFilterWeights0 = boxFilterWeights0;
                this.boxFilterWeights1 = boxFilterWeights1;
            }

            public readonly void SetInputs(RenderPass pass)
            {
            }

            public readonly void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector(command, "_Jitter", jitter);
                pass.SetVector(command, "_PreviousJitter", previousJitter);
                pass.SetFloat(command, "_CrossWeightSum", crossWeightSum);
                pass.SetFloat(command, "_BoxWeightSum", boxWeightSum);
                pass.SetFloat(command, "_CenterCrossFilterWeight", centerCrossFilterWeight);
                pass.SetFloat(command, "_CenterBoxFilterWeight", centerBoxFilterWeight);
                pass.SetVector(command, "_CrossFilterWeights", crossFilterWeights);
                pass.SetVector(command, "_BoxFilterWeights0", boxFilterWeights0);
                pass.SetVector(command, "_BoxFilterWeights1", boxFilterWeights1);
            }
        }

        public void OnPreRender(int scaledWidth, int scaledHeight)
        {
            var sampleIndex = renderGraph.FrameIndex % settings.SampleCount + 1;

            Vector2 jitter;
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

            var result = new TemporalAAData
            (
                jitter: new Vector4(jitter.x, jitter.y, jitter.x / scaledWidth, jitter.y / scaledHeight),
                previousJitter: new Vector4(previousJitter.x, previousJitter.y, previousJitter.x / scaledWidth, previousJitter.y / scaledHeight), // TODO: previous width/height?
                boxWeightSum: boxWeightSum,
                crossWeightSum: crossWeightSum,
                centerCrossFilterWeight: weights[4] * rcpCrossWeightSum,
                centerBoxFilterWeight: weights[4] * rcpBoxWeightSum,
                crossFilterWeights: new Vector4(weights[1], weights[3], weights[5], weights[7]) * rcpCrossWeightSum,
                boxFilterWeights0: new Vector4(weights[0], weights[1], weights[2], weights[3]) * rcpBoxWeightSum,
                boxFilterWeights1: new Vector4(weights[5], weights[6], weights[7], weights[8]) * rcpBoxWeightSum
            );

            ArrayPool<float>.Release(weights);

            renderGraph.ResourceMap.SetRenderPassData<TemporalAAData>(result, renderGraph.FrameIndex);
        }

        public RTHandle Render(Camera camera, RTHandle input, RTHandle motion, float scale)
        {
            if (!settings.IsEnabled)
                return input;

            var (current, history, wasCreated) = textureCache.GetTextures(camera.pixelWidth, camera.pixelHeight, camera);
            var result = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.B10G11R11_UFloatPack32);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal AA"))
            {
                var keyword = scale < 1.0f ? "UPSCALE" : null;
                pass.Initialize(material, 0, 1, keyword);

                pass.ReadTexture("_Input", input);
                pass.ReadTexture("_Velocity", motion);
                pass.ReadTexture("_History", history);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
                pass.AddRenderPassData<TemporalAAData>();

                pass.SetRenderFunction((
                    spatialSharpness: settings.SpatialSharpness,
                    motionSharpness: settings.MotionSharpness * 0.8f,
                    hasHistory: wasCreated ? 0.0f : 1.0f,
                    stationaryBlending: settings.StationaryBlending,
                    motionBlending: settings.MotionBlending,
                    motionWeight: settings.MotionWeight,
                    scale: scale,
                    resolution: new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight),
                    maxWidth: Mathf.FloorToInt(camera.pixelWidth * scale) - 1,
                    maxHeight: Mathf.FloorToInt(camera.pixelHeight * scale) - 1,
                    maxResolution: new Vector2(camera.pixelWidth - 1, camera.pixelHeight - 1)
                ),
                (command, pass, data) =>
                {
                    pass.SetFloat(command, "_SpatialSharpness", data.spatialSharpness);
                    pass.SetFloat(command, "_MotionSharpness", data.motionSharpness);
                    pass.SetFloat(command, "_HasHistory", data.hasHistory);
                    pass.SetFloat(command, "_StationaryBlending", data.stationaryBlending);
                    pass.SetFloat(command, "_VelocityBlending", data.motionBlending);
                    pass.SetFloat(command, "_VelocityWeight", data.motionWeight);
                    pass.SetFloat(command, "_Scale", data.scale);

                    pass.SetVector(command, "_HistoryScaleLimit", new Vector4(history.Scale.x, history.Scale.y, history.Limit.x, history.Limit.y));

                    pass.SetVector(command, "_Resolution", data.resolution);
                    pass.SetVector(command, "_MaxResolution", data.maxResolution);

                    pass.SetInt(command, "_MaxWidth", data.maxWidth);
                    pass.SetInt(command, "_MaxHeight", data.maxHeight);
                });
            }

            return result;
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