using System;
using UnityEngine;
using UnityEngine.Rendering;
using static Unmath.Math;

namespace CustomRenderPipeline
{
    public class LightCulling : ViewRenderFeature
    {
        private readonly LightingSettings settings;
        private readonly ComputeShader computeShader;

        public LightCulling(LightingSettings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            computeShader = Resources.Load<ComputeShader>("LightCulling");
        }

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            var tileCountX = DivRoundUp(viewPassData.viewSize.x, settings.TileSize);
            var tileCountY = DivRoundUp(viewPassData.viewSize.y, settings.TileSize);
            var tileCount = tileCountX * tileCountY;

            var pointLightData = renderGraph.GetResource<PointLightData>();
            var pointLightCount = pointLightData.lightCount;

            var lightIndexCount = DivRoundUp(pointLightCount, 32);
            var visibleLightBits = renderGraph.GetBuffer(Max(1, lightIndexCount * tileCount));

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