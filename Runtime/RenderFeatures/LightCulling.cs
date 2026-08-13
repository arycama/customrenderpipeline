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
            if (settings.PointLightMesh == null || !renderGraph.TryGetResource<PointLightData>(out var pointLightData))
                return;

            void RenderPass(int count, int indexOffset, int passIndex, Int2 viewSize, int viewCount)
            {
                using var pass = renderGraph.AddDrawInstancedProceduralRenderPass("Light Culling", (pointLightData, indexOffset, uavSlot));
                pass.Initialize(settings.PointLightMesh, 0, pointLightMaterial, count, viewSize, viewCount, passIndex: passIndex);
                pass.WriteRtHandleDepth<CameraDepth>();

                pass.ReadResource<ViewData>();
                pass.ReadResource<PointLightData>();
                pass.ReadTexture("VisibleLightBitsWrite", pointLightData.visibleLightBits);

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    command.SetRandomWriteTarget(data.uavSlot, pass.GetRenderTexture(data.pointLightData.visibleLightBits));
                    pass.SetInt("IndexOffset", data.indexOffset);
                });
            }

            var intersectingLightCount = pointLightData.intersectingLightCount;
            if (intersectingLightCount > 0)
                RenderPass(intersectingLightCount, 0, 0, viewPassData.viewSize, viewPassData.viewCount);

            var remainingLightCount = pointLightData.lightCount - intersectingLightCount;
            if (remainingLightCount > 0)
                RenderPass(remainingLightCount, intersectingLightCount, 1, viewPassData.viewSize, viewPassData.viewCount);
        }
    }
}