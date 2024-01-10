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

        private Settings settings;
        private CameraTextureCache textureCache = new();
        private Material material;
        private MaterialPropertyBlock propertyBlock;

        public TemporalAA(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Temporal AA")) { hideFlags = HideFlags.HideAndDontSave };
            propertyBlock = new();
        }

        public void Release()
        {
            textureCache.Dispose();
        }

        public void OnPreRender(Camera camera, float scale, out Matrix4x4 previousMatrix)
        {
            var scaledWidth = (int)(camera.pixelWidth * scale);
            var scaledHeight = (int)(camera.pixelHeight * scale);

            previousMatrix = camera.nonJitteredProjectionMatrix;

            camera.ResetProjectionMatrix();
            camera.nonJitteredProjectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * camera.worldToCameraMatrix;

            var sampleIndex = Time.renderedFrameCount % settings.SampleCount;

            Vector2 jitter;
            jitter.x = Halton(sampleIndex + 1, 2) - 0.5f;
            jitter.y = Halton(sampleIndex + 1, 3) - 0.5f;
            jitter *= settings.JitterSpread;

            var matrix = camera.projectionMatrix;
            matrix[0, 2] = 2.0f * jitter.x / scaledWidth;
            matrix[1, 2] = 2.0f * jitter.y / scaledHeight;
            camera.projectionMatrix = matrix;

            renderGraph.AddRenderPass((command, context) => command.SetGlobalVector("_Jitter", jitter));
        }

        public RTHandle Render(Camera camera, RTHandle input, RTHandle motion, float scale)
        {
            var descriptor = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, RenderTextureFormat.RGB111110Float);
            var wasCreated = textureCache.GetTexture(camera, descriptor, out var current, out var previous);

            renderGraph.AddRenderPass((command, context) =>
            {
                using var profilerScope = command.BeginScopedSample("Temporal AA");

                propertyBlock.SetFloat("_Sharpness", settings.Sharpness);
                propertyBlock.SetFloat("_HasHistory", wasCreated ? 0f : 1f);

                propertyBlock.SetFloat("_StationaryBlending", settings.StationaryBlending);
                propertyBlock.SetFloat("_MotionBlending", settings.MotionBlending);
                propertyBlock.SetFloat("_MotionWeight", settings.MotionWeight);
                propertyBlock.SetFloat("_Scale", scale);

                propertyBlock.SetTexture("_History", previous);

                command.SetGlobalTexture("_Input", input);
                command.SetGlobalTexture("_Motion", motion);

                command.SetRenderTarget(current);
                command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, propertyBlock);
            });

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