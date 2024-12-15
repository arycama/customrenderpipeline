using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ClusteredLightCulling : RenderFeature<(int width, int height, float near, float far)>
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
                this.lightClusterIndices = lightClusterIndices ?? throw new ArgumentNullException(nameof(lightClusterIndices));
                this.lightList = lightList ?? throw new ArgumentNullException(nameof(lightList));
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
                pass.SetFloat(command, "_ClusterScale", clusterScale);
                pass.SetFloat(command, "_ClusterBias", clusterBias);
                pass.SetInt(command, "_TileSize", tileSize);
            }
        }

        public override void Render((int width, int height, float near, float far) data)
        {
            var clusterWidth = MathUtils.DivRoundUp(data.width, settings.TileSize);
            var clusterHeight = MathUtils.DivRoundUp(data.height, settings.TileSize);
            var clusterCount = clusterWidth * clusterHeight * settings.ClusterDepth;

            var clusterScale = settings.ClusterDepth / Mathf.Log(data.far / data.near, 2f);
            var clusterBias = -(settings.ClusterDepth * Mathf.Log(data.near, 2f) / Mathf.Log(data.far / data.near, 2f));

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
                    command.SetBufferData(data.counterBuffer, zeroArray);
                    pass.SetInt(command, "_TileSize", data.tileSize);
                    pass.SetFloat(command, "_RcpClusterDepth", data.rcpClusterDepth);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new Result(lightClusterIndices, lightList, clusterScale, clusterBias, settings.TileSize), renderGraph.FrameIndex);
        }
    }
}