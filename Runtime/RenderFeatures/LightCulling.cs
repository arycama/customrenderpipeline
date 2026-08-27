using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unmath;

namespace CustomRenderPipeline
{
    public class LightCulling : ViewRenderFeature
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField, Pow2(128)] public int TileSize { get; private set; } = 16;
            [field: SerializeField, Pow2(8192)] public int DepthSlices { get; private set; } = 8192;
            [field: SerializeField] public Mesh PointLightMesh { get; private set; }
            [field: SerializeField] public Mesh SpotLightMesh { get; private set; }
        }

        private readonly Settings settings;
        private readonly Material pointLightMaterial;
        private readonly int uavSlot;

        public LightCulling(Settings settings, RenderGraph renderGraph, string pointLightShader = "Hidden/Point Light", int uavSlot = 0) : base(renderGraph)
        {
            this.settings = settings;
            this.uavSlot = uavSlot;
            pointLightMaterial = new Material(Shader.Find(pointLightShader)) { hideFlags = HideFlags.HideAndDontSave };
        }

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            if (settings.PointLightMesh == null || settings.SpotLightMesh == null || !renderGraph.TryGetResource<PointLightData>(out var pointLightData))
                return;

            void RenderPass(Mesh mesh, int count, int indexOffset, int passIndex, Int2 viewSize, int viewCount)
            {
                using var pass = renderGraph.AddDrawInstancedProceduralRenderPass("Light Culling", (pointLightData, indexOffset));
                pass.Initialize(mesh, 0, pointLightMaterial, count, viewSize, viewCount, passIndex);
                pass.WriteRtHandleDepth<CameraDepth>(SubPassFlags.ReadOnlyDepthStencil);
                pass.ReadFrameDepth<CameraDepth>();

                pass.ReadResource<ViewData>();
                pass.ReadResource<PointLightData>();
                pass.ReadTexture("VisibleLightBitsWrite", pointLightData.visibleLightBits);

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    command.SetRandomWriteTarget(0, pass.GetRenderTexture(data.pointLightData.visibleLightBits));
                    pass.SetInt("IndexOffset", data.indexOffset);
                });
            }

            // Intersecting point lights
            if (pointLightData.intersectingPointLightCount > 0)
                RenderPass(settings.PointLightMesh, pointLightData.intersectingPointLightCount, 0, 0, viewPassData.viewSize, viewPassData.viewCount);

            // Non intersecting point lights
            var remainingPointLightCount = pointLightData.pointLightCount - pointLightData.intersectingPointLightCount;
            if (remainingPointLightCount > 0)
                RenderPass(settings.PointLightMesh, remainingPointLightCount, pointLightData.intersectingPointLightCount, 1, viewPassData.viewSize, viewPassData.viewCount);

            // Intersecting spot lights
            if (pointLightData.intersectingSpotLightCount > 0)
                RenderPass(settings.SpotLightMesh, pointLightData.intersectingSpotLightCount, 0, 2, viewPassData.viewSize, viewPassData.viewCount);

            // Non intersecting point lights
            var remainingSpotLightCount = pointLightData.spotLightCount - pointLightData.intersectingSpotLightCount;
            if (remainingSpotLightCount > 0)
                RenderPass(settings.SpotLightMesh, remainingSpotLightCount, pointLightData.intersectingSpotLightCount, 3, viewPassData.viewSize, viewPassData.viewCount);
        }
    }
}