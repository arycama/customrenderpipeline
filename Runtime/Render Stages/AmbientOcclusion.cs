using System;
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

        private Settings settings;
        private Material material;

        public AmbientOcclusion(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Ambient Occlusion")) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Render(Camera camera, RTHandle depth, RTHandle scene, float scale)
        {
            if (settings.Strength == 0.0f)
                return;

            var scaledWidth = (int)(camera.pixelWidth * scale);
            var scaledHeight = (int)(camera.pixelHeight * scale);
            var normals = renderGraph.GetTexture(scaledWidth, scaledHeight, GraphicsFormat.A2B10G10R10_UNormPack32);
            var viewDepth = renderGraph.GetTexture(scaledWidth, scaledHeight, GraphicsFormat.R16_SFloat);

            renderGraph.AddRenderPass((command, context) =>
            {
                command.SetGlobalVector("ScaleOffset", new Vector2(1.0f / scaledWidth, 1.0f / scaledHeight));

                command.SetRenderTarget(new RenderTargetBinding(
                    new[] { new RenderTargetIdentifier(normals), new RenderTargetIdentifier(viewDepth) },
                    new[] { RenderBufferLoadAction.DontCare, RenderBufferLoadAction.DontCare },
                    new[] { RenderBufferStoreAction.Store, RenderBufferStoreAction.Store },
                    depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare)
                { flags = RenderTargetFlags.ReadOnlyDepthStencil });

                command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);

                var tanHalfFovY = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                var tanHalfFovX = tanHalfFovY * camera.aspect;
                command.SetGlobalVector("_UvToView", new Vector4(tanHalfFovX * 2f, tanHalfFovY * 2f, -tanHalfFovX, -tanHalfFovY));

                command.SetGlobalVector("_Tint", settings.Tint.linear);
                command.SetGlobalFloat("_Radius", settings.Radius * scaledHeight / tanHalfFovY * 0.5f);
                command.SetGlobalFloat("_AoStrength", settings.Strength);
                command.SetGlobalFloat("_FalloffScale", settings.Falloff == 1f ? 0f : 1f / (settings.Radius * settings.Falloff - settings.Radius));
                command.SetGlobalFloat("_FalloffBias", settings.Falloff == 1f ? 1f : 1f / (1f - settings.Falloff));
                command.SetGlobalInt("_DirectionCount", settings.DirectionCount);
                command.SetGlobalInt("_SampleCount", settings.SampleCount);

                command.SetGlobalTexture("_ViewDepth", viewDepth);
                command.SetGlobalTexture("_ViewNormals", normals);
                command.SetGlobalTexture("_CameraDepth", depth);
                command.SetRenderTarget(new RenderTargetBinding(scene, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare, depth, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare) { flags = RenderTargetFlags.ReadOnlyDepthStencil });
                command.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);

                if (RenderSettings.fog)
                    command.DrawProcedural(Matrix4x4.identity, material, 2, MeshTopology.Triangles, 3);
            });
        }
    }
}