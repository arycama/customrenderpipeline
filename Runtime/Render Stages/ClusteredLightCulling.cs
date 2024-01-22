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

        private int DivRoundUp(int x, int y) => (x + y - 1) / y;

        public ClusteredLightCulling(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
        }

        public void Render(int width, int height, float near, float far)
        {
            var clusterWidth = DivRoundUp(width, settings.TileSize);
            var clusterHeight = DivRoundUp(height, settings.TileSize);
            var clusterCount = clusterWidth * clusterHeight * settings.ClusterDepth;

            var clusterScale = settings.ClusterDepth / Mathf.Log(far / near, 2f);
            var clusterBias = -(settings.ClusterDepth * Mathf.Log(near, 2f) / Mathf.Log(far / near, 2f));

            var computeShader = Resources.Load<ComputeShader>("ClusteredLightCulling");
            var lightClusterIndices = renderGraph.GetTexture(clusterWidth, clusterHeight, GraphicsFormat.R32G32_SInt, true, settings.ClusterDepth, TextureDimension.Tex3D);

            var lightList = renderGraph.GetBuffer(clusterCount * settings.MaxLightsPerTile);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>())
            {
                pass.RenderPass.Initialize(computeShader, 0, clusterWidth, clusterHeight, settings.ClusterDepth);
                pass.RenderPass.ReadTexture("_LightClusterIndicesWrite", lightClusterIndices);
                pass.RenderPass.WriteBuffer("_LightClusterListWrite", lightList);

                var counterBuffer = renderGraph.GetBuffer();
                pass.RenderPass.WriteBuffer("_LightCounter", counterBuffer);

                pass.RenderPass.SetRenderFunction((command, context) =>
                {
                    command.SetBufferData(counterBuffer, zeroArray);
                    pass.RenderPass.SetInt(command, "_TileSize", settings.TileSize);
                    pass.RenderPass.SetFloat(command, "_RcpClusterDepth", 1.0f / settings.ClusterDepth);
                });
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>())
            {
                pass.RenderPass.SetRenderFunction((command, context) =>
                {
                    // TODO: Handle this with proper pass inputs/outputs
                    command.SetGlobalTexture("_LightClusterIndices", lightClusterIndices);
                    command.SetGlobalBuffer("_LightClusterList", lightList);
                    command.SetGlobalFloat("_ClusterScale", clusterScale);
                    command.SetGlobalFloat("_ClusterBias", clusterBias);
                    command.SetGlobalInt("_TileSize", settings.TileSize);
                });
            }
        }
    }
}