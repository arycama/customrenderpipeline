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
                pass.Material = material;
                pass.Index = 0;

                pass.ReadTexture("_CameraDepth", depth);

                pass.WriteDepth("", depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, 1.0f, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture("", normals, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                pass.WriteTexture("", viewDepth, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

                pass.SetRenderFunction((command, context) =>
                {
                    pass.SetVector(command, "ScaleOffset", new Vector2(1.0f / scaledWidth, 1.0f / scaledHeight));
                });
            }

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>())
            {
                pass.Material = material;
                pass.Index = 1;

                pass.ReadTexture("_ViewDepth", viewDepth);
                pass.ReadTexture("_ViewNormals", normals);
                pass.ReadTexture("_CameraDepth", depth);
                pass.WriteTexture("", scene, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                pass.WriteDepth("", depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, 1.0f, RenderTargetFlags.ReadOnlyDepthStencil);

                pass.SetRenderFunction((command, context) =>
                {
                    pass.SetVector(command, "ScaleOffset", new Vector2(1.0f / scaledWidth, 1.0f / scaledHeight));
                    var tanHalfFovY = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    var tanHalfFovX = tanHalfFovY * camera.aspect;

                    pass.SetVector(command, "_UvToView", new Vector4(tanHalfFovX * 2f, tanHalfFovY * 2f, -tanHalfFovX, -tanHalfFovY));
                    pass.SetVector(command, "_Tint", settings.Tint.linear);
                    pass.SetFloat(command, "_Radius", settings.Radius * scaledHeight / tanHalfFovY * 0.5f);
                    pass.SetFloat(command, "_AoStrength", settings.Strength);
                    pass.SetFloat(command, "_FalloffScale", settings.Falloff == 1f ? 0f : 1f / (settings.Radius * settings.Falloff - settings.Radius));
                    pass.SetFloat(command, "_FalloffBias", settings.Falloff == 1f ? 1f : 1f / (1f - settings.Falloff));
                    pass.SetInt(command, "_DirectionCount", settings.DirectionCount);
                    pass.SetInt(command, "_SampleCount", settings.SampleCount);
                });
            }

            if (RenderSettings.fog)
            {
                using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>();
                pass.Material = material;
                pass.Index = 2;
                pass.ReadTexture("_VolumetricLighting", volumetricLighting);
            }
        }
    }
}