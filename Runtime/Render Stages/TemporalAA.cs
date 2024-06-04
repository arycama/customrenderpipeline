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
            [field: SerializeField, Range(1, 32)] public int SampleCount { get; private set; } = 8;
            [field: SerializeField, Range(0.0f, 1.0f)] public float JitterSpread { get; private set; } = 1.0f;
            [field: SerializeField, Range(0f, 1f)] public float Sharpness { get; private set; } = 0.5f;
            [field: SerializeField, Range(0f, 0.99f)] public float StationaryBlending { get; private set; } = 0.95f;
            [field: SerializeField, Range(0f, 0.99f)] public float MotionBlending { get; private set; } = 0.85f;
            [field: SerializeField] public float MotionWeight { get; private set; } = 6000f;
            [field: SerializeField] public bool JitterOverride { get; private set; } = false;
            [field: SerializeField] public Vector2 JitterOverrideValue { get; private set; } = Vector2.zero;

            [Range(0, 2)]
            public float taaSharpenStrength = 0.5f;

            /// <summary>Larger is this value, more likely history will be rejected when current and reprojected history motion vector differ by a substantial amount.
            /// Larger values can decrease ghosting but will also reintroduce aliasing on the aforementioned cases.</summary>
            [Range(0.0f, 1.0f)]
            public float taaMotionVectorRejection = 0.0f;

            /// <summary>Drive the anti-flicker mechanism. With high values flickering might be reduced, but it can lead to more ghosting or disocclusion artifacts.</summary>
            [Range(0.0f, 1.0f)]
            public float taaAntiFlicker = 0.5f;

            /// <summary> Determines how much the history buffer is blended together with current frame result. Higher values means more history contribution. </summary>
            [Range(0.6f, 0.95f)]
            public float taaBaseBlendFactor = 0.875f;
        }

        private readonly Settings settings;
        private readonly PersistentRTHandleCache textureCache;
        private readonly Material material;

        public TemporalAA(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Temporal AA")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Temporal AA");
        }

        public struct TemporalAAData : IRenderPassData
        {
            private readonly Vector4 jitter;
            private readonly Vector4 previousJitter;
            private readonly float maxCrossWeight;
            private readonly float maxBoxWeight;
            private readonly float centerCrossFilterWeight;
            private readonly float centerBoxFilterWeight;
            private readonly Vector4 crossFilterWeights;
            private readonly Vector4 boxFilterWeights0;
            private readonly Vector4 boxFilterWeights1;

            public readonly Vector4 Jitter => jitter;

            public TemporalAAData(Vector4 jitter, Vector4 previousJitter, float maxCrossWeight, float maxBoxWeight, float centerCrossFilterWeight, float centerBoxFilterWeight, Vector4 crossFilterWeights, Vector4 boxFilterWeights0, Vector4 boxFilterWeights1)
            {
                this.jitter = jitter;
                this.previousJitter = previousJitter;
                this.maxCrossWeight = maxCrossWeight;
                this.maxBoxWeight = maxBoxWeight;
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
                pass.SetFloat(command, "_MaxCrossWeight", maxCrossWeight);
                pass.SetFloat(command, "_MaxBoxWeight", maxBoxWeight);
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

                    //weight = Mathf.Clamp01(1.0f - Mathf.Abs(x + jitter.x));
                    //weight *= Mathf.Clamp01(1.0f - Mathf.Abs(y + jitter.y));

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
                maxCrossWeight: maxCrossWeight,
                maxBoxWeight: maxBoxWeight,
                centerCrossFilterWeight: weights[4] * rcpCrossWeightSum,
                centerBoxFilterWeight: weights[4] * rcpBoxWeightSum,
                crossFilterWeights: new Vector4(weights[1], weights[3], weights[5], weights[7]) * rcpCrossWeightSum,
                boxFilterWeights0: new Vector4(weights[0], weights[1], weights[2], weights[3]) * rcpBoxWeightSum,
                boxFilterWeights1: new Vector4(weights[5], weights[6], weights[7], weights[8]) * rcpBoxWeightSum
            );

            ArrayPool<float>.Release(weights);

            renderGraph.ResourceMap.SetRenderPassData<TemporalAAData>(result, renderGraph.FrameIndex);
        }

        private class PassData
        {
            internal float sharpness;
            internal float hasHistory;
            internal float stationaryBlending;
            internal float motionBlending;
            internal float motionWeight;
            internal float scale;
            internal Vector4 scaledResolution;
            internal Vector4 resolution;
            internal int maxWidth;
            internal int maxHeight;
            internal Vector2 maxResolution;
            internal float antiFlickerIntensity;
            internal float contrastForMaxAntiFlicker;
            internal float baseBlendFactor;
            internal float historyContrastBlendLerp;
            internal float motionRejectionMultiplier;
        }

        public RTHandle Render(Camera camera, RTHandle input, RTHandle motion, float scale)
        {
            if (!settings.IsEnabled)
                return input;

            var descriptor = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float);
            var (current, history, wasCreated) = textureCache.GetTextures(camera.pixelWidth, camera.pixelHeight, camera);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal AA"))
            {
                var keyword = scale < 1.0f ? "UPSCALE" : null;
                pass.Initialize(material, 0, 1, keyword);

                pass.ReadTexture("_Input", input);
                pass.ReadTexture("_Velocity", motion);
                pass.ReadTexture("_History", history);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
                pass.AddRenderPassData<TemporalAAData>();

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_Sharpness", data.sharpness);
                    pass.SetFloat(command, "_HasHistory", data.hasHistory);
                    pass.SetFloat(command, "_StationaryBlending", data.stationaryBlending);
                    pass.SetFloat(command, "_VelocityBlending", data.motionBlending);
                    pass.SetFloat(command, "_VelocityWeight", data.motionWeight);
                    pass.SetFloat(command, "_Scale", data.scale);

                    pass.SetFloat(command, "_AntiFlickerIntensity", data.antiFlickerIntensity);
                    pass.SetFloat(command, "_ContrastForMaxAntiFlicker", data.contrastForMaxAntiFlicker);
                    pass.SetFloat(command, "_BaseBlendFactor", data.baseBlendFactor);
                    pass.SetFloat(command, "_HistoryContrastBlendLerp", data.historyContrastBlendLerp);
                    pass.SetFloat(command, "_SharpenStrength", settings.taaSharpenStrength);
                    pass.SetFloat(command, "_SpeedRejectionIntensity", data.motionRejectionMultiplier);
                    pass.SetVector(command, "_HistoryScaleLimit", new Vector4(history.Scale.x, history.Scale.y, history.Limit.x, history.Limit.y));

                    pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                    pass.SetVector(command, "_Resolution", data.resolution);
                    pass.SetVector(command, "_MaxResolution", data.maxResolution);

                    pass.SetInt(command, "_MaxWidth", data.maxWidth);
                    pass.SetInt(command, "_MaxHeight", data.maxHeight);
                });

                data.sharpness = settings.Sharpness * 0.8f;
                data.hasHistory = wasCreated ? 0.0f : 1.0f;
                data.stationaryBlending = settings.StationaryBlending;
                data.motionBlending = settings.MotionBlending;
                data.motionWeight = settings.MotionWeight;
                data.scale = scale;
                data.scaledResolution = new Vector4(camera.pixelWidth * scale, camera.pixelHeight * scale, 1.0f / (camera.pixelWidth * scale), 1.0f / (camera.pixelHeight * scale));
                data.resolution = new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight);
                data.maxWidth = Mathf.FloorToInt(camera.pixelWidth * scale) - 1;
                data.maxHeight = Mathf.FloorToInt(camera.pixelHeight * scale) - 1;
                data.maxResolution = new Vector2(camera.pixelWidth - 1, camera.pixelHeight - 1);

                float minAntiflicker = 0.0f;
                float maxAntiflicker = 3.5f;
                data.motionRejectionMultiplier = Mathf.Lerp(0.0f, 250.0f, settings.taaMotionVectorRejection * settings.taaMotionVectorRejection * settings.taaMotionVectorRejection);

                // The anti flicker becomes much more aggressive on higher values
                float temporalContrastForMaxAntiFlicker = 0.7f - Mathf.Lerp(0.0f, 0.3f, Mathf.SmoothStep(0.5f, 1.0f, settings.taaAntiFlicker));

                //float historySharpening = TAAU && postDoF ? 0.25f : settings.taaHistorySharpening;

                float antiFlicker = Mathf.Lerp(minAntiflicker, maxAntiflicker, settings.taaAntiFlicker);
                const float historyContrastBlendStart = 0.51f;
                data.historyContrastBlendLerp = Mathf.Clamp01((settings.taaAntiFlicker - historyContrastBlendStart) / (1.0f - historyContrastBlendStart));

                data.antiFlickerIntensity = antiFlicker;
                data.contrastForMaxAntiFlicker = temporalContrastForMaxAntiFlicker;

                // For post dof we can be a bit more agressive with the taa base blend factor, since most aliasing has already been taken care of in the first TAA pass.
                // The following MAD operation expands the range to a new minimum (and keeps max the same).
                data.baseBlendFactor = settings.taaBaseBlendFactor;
            }

            return current;
        }

        public static float Halton(int index, int radix)
        {
            float result = 0f;
            float fraction = 1f / (float)radix;

            while (index > 0)
            {
                result += (float)(index % radix) * fraction;

                index /= radix;
                fraction /= (float)radix;
            }

            return result;
        }

        float Mitchell1D(float x)
        {
            var B = 1.0f / 3.0f;
            var C = 1.0f / 3.0f;
            x = Mathf.Abs(x);

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