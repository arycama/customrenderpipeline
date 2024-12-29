using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ClusteredLightCulling : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [SerializeField] private int tileSize = 16;
            [SerializeField] private int clusterDepth = 32;
            [SerializeField] private int maxLightsPerTile = 32;

            public int TileSize => tileSize;
            public int ClusterDepth => clusterDepth;
            public int MaxLightsPerTile => maxLightsPerTile;
        }

        private readonly Settings settings;

        private static readonly uint[] zeroArray = new uint[1] { 0 };

        public ClusteredLightCulling(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
        }

        public readonly struct Result : IRenderPassData
        {
            private readonly RTHandle lightClusterIndices;
            private readonly BufferHandle lightList;
            private readonly float clusterScale, clusterBias;
            private readonly int tileSize;

            public Result(RTHandle lightClusterIndices, BufferHandle lightList, float clusterScale, float clusterBias, int tileSize)
            {
                this.lightClusterIndices = lightClusterIndices;
                this.lightList = lightList;
                this.clusterScale = clusterScale;
                this.clusterBias = clusterBias;
                this.tileSize = tileSize;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_LightClusterIndices", lightClusterIndices);
                pass.ReadBuffer("_LightClusterList", lightList);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetFloat("_ClusterScale", clusterScale);
                pass.SetFloat("_ClusterBias", clusterBias);
                pass.SetInt("_TileSize", tileSize);
            }
        }

        public override void Render()
        {
            var viewData = renderGraph.GetResource<ViewData>();

            var clusterWidth = MathUtils.DivRoundUp(viewData.ScaledWidth, settings.TileSize);
            var clusterHeight = MathUtils.DivRoundUp(viewData.ScaledHeight, settings.TileSize);
            var clusterCount = clusterWidth * clusterHeight * settings.ClusterDepth;

            var clusterScale = settings.ClusterDepth / Mathf.Log(viewData.Far / viewData.Near, 2f);
            var clusterBias = -(settings.ClusterDepth * Mathf.Log(viewData.Near, 2f) / Mathf.Log(viewData.Far / viewData.Near, 2f));

            var computeShader = Resources.Load<ComputeShader>("ClusteredLightCulling");
            var lightClusterIndices = renderGraph.GetTexture(clusterWidth, clusterHeight, GraphicsFormat.R32G32_SInt, settings.ClusterDepth, TextureDimension.Tex3D);

            var lightList = renderGraph.GetBuffer(clusterCount * settings.MaxLightsPerTile);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Clustered Light Culling"))
            {
                pass.Initialize(computeShader, 0, clusterWidth, clusterHeight, settings.ClusterDepth);
                pass.AddRenderPassData<LightingSetup.Result>();
                var counterBuffer = renderGraph.GetBuffer();

                pass.WriteBuffer("_LightClusterListWrite", lightList);
                pass.WriteBuffer("_LightCounter", counterBuffer);
                pass.ReadBuffer("_LightCounter", counterBuffer);
                pass.WriteTexture("_LightClusterIndicesWrite", lightClusterIndices);
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction(
                (
                    tileSize: settings.TileSize,
                    rcpClusterDepth: 1.0f / settings.ClusterDepth,
                    counterBuffer: counterBuffer
                ),

                (command, pass, data) =>
                {
                    command.SetBufferData(pass.GetBuffer(data.counterBuffer), zeroArray);
                    pass.SetInt("_TileSize", data.tileSize);
                    pass.SetFloat("_RcpClusterDepth", data.rcpClusterDepth);
                });
            }

            renderGraph.SetResource(new Result(lightClusterIndices, lightList, clusterScale, clusterBias, settings.TileSize)); ;
        }
    }
}