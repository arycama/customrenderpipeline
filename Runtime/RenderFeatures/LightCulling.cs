using System;
using UnityEngine;
using UnityEngine.Rendering;
using static Unmath.Math;

namespace CustomRenderPipeline
{
    public class LightCulling : ViewRenderFeature
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField, Pow2(128)] public int TileSize { get; private set; } = 16;
            [field: SerializeField, Pow2(8192)] public int DepthSlices { get; private set; } = 8192;
        }

        private readonly Settings settings;
        private readonly ComputeShader computeShader;

        public LightCulling(Settings settings, RenderGraph renderGraph, ComputeShader computeShader) : base(renderGraph)
        {
            this.settings = settings;
            this.computeShader = computeShader;
        }

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            if (!renderGraph.TryGetResource<PointLightData>(out var pointLightData))
                return;

            var tileCountX = DivRoundUp(viewPassData.viewSize.x, settings.TileSize);
            var tileCountY = DivRoundUp(viewPassData.viewSize.y, settings.TileSize);
            var tileCount = tileCountX * tileCountY;

            var lightIndexCount = DivRoundUp(pointLightData.lightCount, 32);
            var visibleLightBits = renderGraph.GetBuffer(lightIndexCount * tileCount);

            using (var pass = renderGraph.AddComputeRenderPass("Light Culling"))
            {
                pass.Initialize(computeShader, 0, tileCountX, tileCountY, viewPassData.viewCount, false);
                pass.ReadResource<PointLightData>();

                pass.WriteBuffer("VisibleLightBitsWrite", visibleLightBits);
                pass.ReadResource<ViewData>();
            }

            renderGraph.SetResource(new Result(visibleLightBits));
        }

        public readonly struct Result : IRenderPassData
        {
            private readonly ResourceHandle<GraphicsBuffer> visibleLightBits;

            public Result(ResourceHandle<GraphicsBuffer> visibleLightBits)
            {
                this.visibleLightBits = visibleLightBits;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadBuffer("VisibleLightBits", visibleLightBits);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
            }
        }
    }
}