using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class AmbientOcclusion : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [SerializeField, Range(0.0f, 8.0f)] private float strength = 1.0f;
            [SerializeField] private Color tint = Color.black;
            [SerializeField] private float radius = 5.0f;
            [SerializeField, Range(0f, 1f)] private float falloff = 0.75f;
            [SerializeField, Range(1, 8)] private int directionCount = 1;
            [SerializeField, Range(1, 32)] private int sampleCount = 8;

            public float Strength => strength;
            public Color Tint => tint;
            public float Radius => radius;
            public float Falloff => falloff;
            public int DirectionCount => directionCount;
            public int SampleCount => sampleCount;
        }

        private readonly Settings settings;
        private readonly Material material;

        public AmbientOcclusion(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Ambient Occlusion")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(Camera camera, RTHandle depth, RTHandle scene, float scale, RTHandle volumetricLighting)
        {
            if (settings.Strength == 0.0f)
                return;

            var scaledWidth = (int)(camera.pixelWidth * scale);
            var scaledHeight = (int)(camera.pixelHeight * scale);
            var normals = renderGraph.GetTexture(scaledWidth, scaledHeight, GraphicsFormat.A2B10G10R10_UNormPack32);
            var viewDepth = renderGraph.GetTexture(scaledWidth, scaledHeight, GraphicsFormat.R16_SFloat);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>())
            {
                pass.RenderPass.Material = material;
                pass.RenderPass.Index = 0;

                pass.RenderPass.ReadTexture("_CameraDepth", depth);

                pass.RenderPass.WriteDepth("", depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, 1.0f, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.RenderPass.WriteTexture("", normals, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                pass.RenderPass.WriteTexture("", viewDepth, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

                pass.RenderPass.SetRenderFunction((command, context) =>
                {
                    pass.RenderPass.SetVector(command, "ScaleOffset", new Vector2(1.0f / scaledWidth, 1.0f / scaledHeight));
                });
            }

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>())
            {
                pass.RenderPass.Material = material;
                pass.RenderPass.Index = 1;

                pass.RenderPass.ReadTexture("_ViewDepth", viewDepth);
                pass.RenderPass.ReadTexture("_ViewNormals", normals);
                pass.RenderPass.ReadTexture("_CameraDepth", depth);
                pass.RenderPass.WriteTexture("", scene, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                pass.RenderPass.WriteDepth("", depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, 1.0f, RenderTargetFlags.ReadOnlyDepthStencil);

                pass.RenderPass.SetRenderFunction((command, context) =>
                {
                    pass.RenderPass.SetVector(command, "ScaleOffset", new Vector2(1.0f / scaledWidth, 1.0f / scaledHeight));
                    var tanHalfFovY = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    var tanHalfFovX = tanHalfFovY * camera.aspect;

                    pass.RenderPass.SetVector(command, "_UvToView", new Vector4(tanHalfFovX * 2f, tanHalfFovY * 2f, -tanHalfFovX, -tanHalfFovY));
                    pass.RenderPass.SetVector(command, "_Tint", settings.Tint.linear);
                    pass.RenderPass.SetFloat(command, "_Radius", settings.Radius * scaledHeight / tanHalfFovY * 0.5f);
                    pass.RenderPass.SetFloat(command, "_AoStrength", settings.Strength);
                    pass.RenderPass.SetFloat(command, "_FalloffScale", settings.Falloff == 1f ? 0f : 1f / (settings.Radius * settings.Falloff - settings.Radius));
                    pass.RenderPass.SetFloat(command, "_FalloffBias", settings.Falloff == 1f ? 1f : 1f / (1f - settings.Falloff));
                    pass.RenderPass.SetInt(command, "_DirectionCount", settings.DirectionCount);
                    pass.RenderPass.SetInt(command, "_SampleCount", settings.SampleCount);
                });
            }

            if (RenderSettings.fog)
            {
                using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>();
                pass.RenderPass.Material = material;
                pass.RenderPass.Index = 2;
                pass.RenderPass.ReadTexture("_VolumetricLighting", volumetricLighting);
            }
        }
    }
}