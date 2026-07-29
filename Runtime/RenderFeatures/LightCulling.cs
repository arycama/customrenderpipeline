using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        private readonly ComputeShader computeShader, depthBinningComputeShader;

        public LightCulling(Settings settings, RenderGraph renderGraph, ComputeShader computeShader) : base(renderGraph)
        {
            this.settings = settings;
            this.computeShader = computeShader;
            this.depthBinningComputeShader = Resources.Load<ComputeShader>("LightDepthBinning");
        }

        public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
        {
            if (!renderGraph.TryGetResource<PointLightData>(out var pointLightData))
                return;

            var tileCountX = DivRoundUp(viewPassData.viewSize.x, settings.TileSize);
            var tileCountY = DivRoundUp(viewPassData.viewSize.y, settings.TileSize);

            var lightIndexCount = DivRoundUp(pointLightData.lightCount, 32);
            var lightDepthRanges = renderGraph.GetTexture(new(settings.DepthSlices, 1), GraphicsFormat.R16G16_UInt);

            using (var pass = renderGraph.AddComputeRenderPass("Light Depth Binning"))
            {
                pass.Initialize(depthBinningComputeShader, 0, settings.DepthSlices, 1, 1, false);
                pass.ReadResource<PointLightData>();

                pass.WriteTexture("Result", lightDepthRanges);
                pass.ReadResource<ViewData>();
            }

            var visibleLightBits = renderGraph.GetTexture(new(tileCountX, tileCountY), GraphicsFormat.R32_UInt, lightIndexCount, TextureDimension.Tex2DArray);
            using (var pass = renderGraph.AddComputeRenderPass("Light Culling"))
            {
                pass.Initialize(computeShader, 0, tileCountX, tileCountY, viewPassData.viewCount, false);
                pass.ReadResource<PointLightData>();

                pass.WriteTexture("VisibleLightBitsWrite", visibleLightBits);
                pass.ReadResource<ViewData>();
            }

            renderGraph.SetResource(new Result(lightDepthRanges, visibleLightBits));
        }

        public readonly struct Result : IRenderPassData
        {
            private readonly ResourceHandle<RenderTexture> lightDepthRanges;
            private readonly ResourceHandle<RenderTexture> visibleLightBits;

            public Result(ResourceHandle<RenderTexture> lightDepthRanges, ResourceHandle<RenderTexture> visibleLightBits)
            {
                this.lightDepthRanges = lightDepthRanges;
                this.visibleLightBits = visibleLightBits;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("LightDepthRanges", lightDepthRanges);
                pass.ReadTexture("VisibleLightBits", visibleLightBits);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
            }
        }
    }
}