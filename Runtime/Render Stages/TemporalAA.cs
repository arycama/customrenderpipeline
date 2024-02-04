using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class TemporalAA : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [SerializeField, Range(1, 32)] private int sampleCount = 8;
            [SerializeField, Range(0.0f, 1f)] private float jitterSpread = 0.75f;
            [SerializeField, Range(0f, 1f)] private float sharpness = 0.5f;
            [SerializeField, Range(0f, 0.99f)] private float stationaryBlending = 0.95f;
            [SerializeField, Range(0f, 0.99f)] private float motionBlending = 0.85f;
            [SerializeField] private float motionWeight = 6000f;

            public int SampleCount => sampleCount;
            public float JitterSpread => jitterSpread;
            public float Sharpness => sharpness;
            public float StationaryBlending => stationaryBlending;
            public float MotionBlending => motionBlending;
            public float MotionWeight => motionWeight;
        }

        private readonly Settings settings;
        private readonly CameraTextureCache textureCache;
        private readonly Material material;

        public TemporalAA(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Temporal AA")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(renderGraph, "Temporal AA");
        }

        public Vector2 Jitter { get; private set; }

        public void Release()
        {
            textureCache.Dispose();
        }

        public void OnPreRender()
        {
            var sampleIndex = Time.renderedFrameCount % settings.SampleCount;

            Vector2 jitter;
            jitter.x = Halton(sampleIndex + 1, 2) - 0.5f;
            jitter.y = Halton(sampleIndex + 1, 3) - 0.5f;
            jitter *= settings.JitterSpread;

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
            var wasCreated = textureCache.GetTexture(camera, descriptor, out var current, out var previous);

            using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal AA");
            pass.Material = material;
            pass.Index = 0;

            pass.ReadTexture("_Input", input);
            pass.ReadTexture("_Motion", motion);
            pass.ReadTexture("_History", previous);
            pass.WriteTexture("", current, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

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
            data.wasCreated = wasCreated ? 0.0f : 1.0f;
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