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
            [field: SerializeField, Range(0.0f, 1f)] public float JitterSpread { get; private set; } = 1.0f;
            [field: SerializeField, Range(0f, 1f)] public float Sharpness { get; private set; } = 0.5f;
            [field: SerializeField, Range(0f, 0.99f)] public float StationaryBlending { get; private set; } = 0.95f;
            [field: SerializeField, Range(0f, 0.99f)] public float MotionBlending { get; private set; } = 0.85f;
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
            textureCache = new(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Temporal AA");
        }

        public Vector2 Jitter { get; private set; }

        public void OnPreRender()
        {
            var sampleIndex = renderGraph.FrameCount % settings.SampleCount;

            Vector2 jitter;
            jitter.x = Halton(sampleIndex + 1, 2) - 0.5f;
            jitter.y = Halton(sampleIndex + 1, 3) - 0.5f;
            jitter *= settings.JitterSpread;

            if(settings.JitterOverride)
                jitter = settings.JitterOverrideValue;

            Jitter = jitter;
        }

        private class PassData
        {
            internal float sharpness;
            internal float wasCreated;
            internal float stationaryBlending;
            internal float motionBlending;
            internal float motionWeight;
            internal float scale;
            internal Vector2 jitter;
            internal Vector4 scaledResolution;
            internal Vector4 resolution;
        }

        public RTHandle Render(Camera camera, RTHandle input, RTHandle motion, float scale)
        {
            var descriptor = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float);
            var (current, history, wasCreated) = textureCache.GetTextures(camera.pixelWidth, camera.pixelHeight, true, camera);

            using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal AA");
            pass.Initialize(material);
            pass.ReadTexture("_Input", input);
            pass.ReadTexture("_Motion", motion);
            pass.ReadTexture("_History", history);
            pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

            var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
            {
                pass.SetFloat(command, "_Sharpness", data.sharpness);
                pass.SetFloat(command, "_HasHistory", data.wasCreated);
                pass.SetFloat(command, "_StationaryBlending", data.stationaryBlending);
                pass.SetFloat(command, "_MotionBlending", data.motionBlending);
                pass.SetFloat(command, "_MotionWeight", data.motionWeight);
                pass.SetFloat(command, "_Scale", data.scale);

                pass.SetVector(command, "_Jitter", data.jitter);
                pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                pass.SetVector(command, "_Resolution", data.resolution);
            });

            data.sharpness = settings.Sharpness;
            data.wasCreated = wasCreated ? 1.0f : 0.0f;
            data.stationaryBlending = settings.StationaryBlending;
            data.motionBlending = settings.MotionBlending;
            data.motionWeight = settings.MotionWeight;
            data.scale = scale;
            data.jitter = Jitter;
            data.scaledResolution = new Vector4(camera.pixelWidth * scale, camera.pixelHeight * scale, 1.0f / (camera.pixelWidth * scale), 1.0f / (camera.pixelHeight * scale));
            data.resolution = new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight);

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
    }
}