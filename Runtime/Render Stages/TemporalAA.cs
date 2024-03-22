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
            [field: SerializeField, Range(1, 32)] public int SampleCount { get; private set; } = 8;
            [field: SerializeField, Range(0.0f, 1.0f)] public float JitterSpread { get; private set; } = 1.0f;
            [field: SerializeField, Range(0f, 1f)] public float Sharpness { get; private set; } = 0.5f;
            [field: SerializeField, Range(0f, 0.99f)] public float StationaryBlending { get; private set; } = 0.95f;
            [field: SerializeField, Range(0f, 0.99f)] public float MotionBlending { get; private set; } = 0.85f;
            [field: SerializeField] public float MotionWeight { get; private set; } = 6000f;
            [field: SerializeField] public FilterMode Filter { get; private set; } = FilterMode.Mitchell;
            [field: SerializeField, Range(0.0f, 4.0f)] public float FilterSize { get; private set; } = 1.5f;
            [field: SerializeField] public bool JitterOverride { get; private set; } = false;
            [field: SerializeField] public Vector2 JitterOverrideValue { get; private set; } = Vector2.zero;

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

            public enum FilterMode
            {
                None,
                //Box,
                Triangle,
                //Gaussian,
                BlackmanHarris,
                Mitchell,
                CatmullRom,
                BSpline,
                //Lanczos
            }
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

        public Vector2 Jitter { get; private set; }

        public void OnPreRender()
        {
            var sampleIndex = renderGraph.FrameIndex % settings.SampleCount;

            Vector2 jitter;
            jitter.x = Halton(sampleIndex + 1, 2) - 0.5f;
            jitter.y = Halton(sampleIndex + 1, 3) - 0.5f;
            jitter *= settings.JitterSpread;

            if (settings.JitterOverride)
                jitter = settings.JitterOverrideValue;

            Jitter = jitter;
        }

        private class PassData
        {
            internal float sharpness;
            internal float hasHistory;
            internal float stationaryBlending;
            internal float motionBlending;
            internal float motionWeight;
            internal float scale;
            internal Vector2 jitter;
            internal Vector4 scaledResolution;
            internal Vector4 resolution;
            internal int maxWidth;
            internal int maxHeight;
            internal Vector2 maxResolution;
            internal float[] colorWeights;
            internal float antiFlickerIntensity;
            internal float contrastForMaxAntiFlicker;
            internal float baseBlendFactor;
            internal float historyContrastBlendLerp;
        }

        public RTHandle Render(Camera camera, RTHandle input, RTHandle motion, float scale)
        {
            var descriptor = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float);
            var (current, history, wasCreated) = textureCache.GetTextures(camera.pixelWidth, camera.pixelHeight, camera);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal AA"))
            {
                pass.Initialize(material);
                pass.ReadTexture("_Input", input);
                pass.ReadTexture("_Velocity", motion);
                pass.ReadTexture("_History", history);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
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

                    pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                    pass.SetVector(command, "_Resolution", data.resolution);
                    pass.SetVector(command, "_MaxResolution", data.maxResolution);
                    pass.SetVector(command, "_Jitter", data.jitter);

                    pass.SetInt(command, "_MaxWidth", data.maxWidth);
                    pass.SetInt(command, "_MaxHeight", data.maxHeight);

                    pass.SetFloatArray(command, "_FilterWeights", data.colorWeights);
                    ArrayPool<float>.Release(data.colorWeights);
                });

                data.sharpness = settings.Sharpness * 0.8f;
                data.hasHistory = wasCreated ? 0.0f : 1.0f;
                data.stationaryBlending = settings.StationaryBlending;
                data.motionBlending = settings.MotionBlending;
                data.motionWeight = settings.MotionWeight;
                data.scale = scale;
                data.jitter = Jitter;
                data.scaledResolution = new Vector4(camera.pixelWidth * scale, camera.pixelHeight * scale, 1.0f / (camera.pixelWidth * scale), 1.0f / (camera.pixelHeight * scale));
                data.resolution = new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight);
                data.maxWidth = Mathf.FloorToInt(camera.pixelWidth * scale) - 1;
                data.maxHeight = Mathf.FloorToInt(camera.pixelHeight) - 1;
                data.maxResolution = new Vector2(camera.pixelWidth - 1, camera.pixelHeight - 1);

                float minAntiflicker = 0.0f;
                float maxAntiflicker = 3.5f;
                float motionRejectionMultiplier = Mathf.Lerp(0.0f, 250.0f, settings.taaMotionVectorRejection * settings.taaMotionVectorRejection * settings.taaMotionVectorRejection);

                // The anti flicker becomes much more aggressive on higher values
                float temporalContrastForMaxAntiFlicker = 0.7f - Mathf.Lerp(0.0f, 0.3f, Mathf.SmoothStep(0.5f, 1.0f, settings.taaAntiFlicker));

                var postDoF = false;
                bool TAAU = false;//camera.IsTAAUEnabled();
                bool runsAfterUpscale = true;// (resGroup == ResolutionGroup.AfterDynamicResUpscale);

                float antiFlickerLerpFactor = settings.taaAntiFlicker;
                //float historySharpening = TAAU && postDoF ? 0.25f : settings.taaHistorySharpening;

                //if (camera.camera.cameraType == CameraType.SceneView)
                //{
                //    // Force settings for scene view.
                //    historySharpening = 0.25f;
                //    antiFlickerLerpFactor = 0.7f;
                //}
                float antiFlicker = postDoF ? maxAntiflicker : Mathf.Lerp(minAntiflicker, maxAntiflicker, antiFlickerLerpFactor);
                const float historyContrastBlendStart = 0.51f;
                float historyContrastLerp = Mathf.Clamp01((antiFlickerLerpFactor - historyContrastBlendStart) / (1.0f - historyContrastBlendStart));

                var colorWeights = ArrayPool<float>.Get(12); // Only need 9, but need 12 for alignment rules
                var weightSum = 0.0f;
                var size = settings.FilterSize * scale;
                //var alpha = settings.FilterAlpha;

                data.antiFlickerIntensity = antiFlicker;
                data.contrastForMaxAntiFlicker = temporalContrastForMaxAntiFlicker;

                // For post dof we can be a bit more agressive with the taa base blend factor, since most aliasing has already been taken care of in the first TAA pass.
                // The following MAD operation expands the range to a new minimum (and keeps max the same).
                const float postDofMin = 0.4f;
                const float TAABaseBlendFactorMin = 0.6f;
                const float TAABaseBlendFactorMax = 0.95f;
                const float scale1 = (TAABaseBlendFactorMax - postDofMin) / (TAABaseBlendFactorMax - TAABaseBlendFactorMin);
                const float offset1 = postDofMin - TAABaseBlendFactorMin * scale1;
                float taaBaseBlendFactor = postDoF ? settings.taaBaseBlendFactor * scale1 + offset1 : settings.taaBaseBlendFactor;
                data.baseBlendFactor = taaBaseBlendFactor;
                data.historyContrastBlendLerp = historyContrastLerp;

                for (int y = -1, i = 0; y <= 1; y++)
                {
                    for (var x = -1; x <= 1; x++, i++)
                    {
                        var xCoord = (x + data.jitter.x) / scale;
                        var yCoord = (y + data.jitter.y) / scale;
                        var d = (xCoord * xCoord + yCoord * yCoord);

                        float weight;
                        switch (settings.Filter)
                        {
                            case Settings.FilterMode.None:
                                weight = (x == 0 && y == 0) ? 1.0f : 0.0f;
                                break;
                            //case Settings.FilterMode.Box:
                            //    weight = 1.0f;
                            //    break;
                            case Settings.FilterMode.Triangle:
                                var deltaX = Mathf.Clamp01(1.0f - Mathf.Abs(xCoord));
                                var deltaY = Mathf.Clamp01(1.0f - Mathf.Abs(yCoord));
                                weight = deltaX * deltaY;
                                break;
                            //case Settings.FilterMode.Gaussian:
                            //    {
                            //        var expV = Mathf.Exp(-alpha * size * size);
                            //        weight = Mathf.Max(0.0f, Mathf.Exp(-alpha * xCoord * xCoord) - expV);
                            //        weight *= Mathf.Max(0.0f, Mathf.Exp(-alpha * yCoord * yCoord) - expV);
                            //    }
                            //    break;
                            case Settings.FilterMode.BlackmanHarris:
                                //weight = 0.35875f - 0.48829f*Mathf.Cos * (Mathf.PI*x/w + pi) + 0.14128*cos(2.*pi*x/w) - 0.01168*cos(3.*pi*x/w + pi*3.);
                                weight = Mathf.Exp(-0.5f / 0.22f * d);
                                break;
                            case Settings.FilterMode.Mitchell:
                                weight = Mitchell1D(xCoord / size, 1.0f / 3.0f, 1.0f / 3.0f) * Mitchell1D(yCoord / size, 1.0f / 3.0f, 1.0f / 3.0f);
                                break;
                            case Settings.FilterMode.CatmullRom:
                                weight = Mitchell1D(xCoord / size, 0.0f, 0.5f) * Mitchell1D(yCoord / size, 0.0f, 0.5f);
                                break;
                            case Settings.FilterMode.BSpline:
                                weight = Mitchell1D(xCoord / size, 1.0f, 0.0f) * Mitchell1D(yCoord / size, 1.0f, 0.0f);
                                break;
                            //case Settings.FilterMode.Lanczos:
                            //    weight = WindowedSinc(xCoord, size, alpha) * WindowedSinc(yCoord, size, alpha);
                            //    break;
                            default:
                                throw new ArgumentException(settings.Filter.ToString());
                        }

                        colorWeights[i] = weight;
                        weightSum += weight;
                    }
                }

                // Normalize weights
                var rcpWeightSum = 1.0f / weightSum;
                for (var i = 0; i < 9; i++)
                {
                    colorWeights[i] *= rcpWeightSum;
                }

                data.colorWeights = colorWeights;
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

        float Mitchell1D(float x, float B, float C)
        {
            x = Mathf.Abs(2 * x);

            if (x <= 1.0f)
                return ((12 - 9 * B - 6 * C) * x * x * x + (-18 + 12 * B + 6 * C) * x * x + (6 - 2 * B)) * (1.0f / 6.0f);
            else if (x <= 2.0f)
                return ((-B - 6 * C) * x * x * x + (6 * B + 30 * C) * x * x + (-12 * B - 48 * C) * x + (8 * B + 24 * C)) * (1.0f / 6.0f);
            else
                return 0.0f;
        }

        float Sinc(float x)
        {
            return x == 0.0f ? 1.0f : Mathf.Sin(Mathf.PI * x) / (Mathf.PI * x);
        }

        float WindowedSinc(float x, float radius, float tau)
        {
            if (x > radius) return 0.0f;
            var lanczos = Sinc(x / tau);
            return Sinc(x) / lanczos;
        }
    }
}