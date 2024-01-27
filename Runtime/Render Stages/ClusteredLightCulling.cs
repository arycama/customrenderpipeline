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

        class Pass0Data
        {
            public int tileSize;
            public float rcpClusterDepth;
            public BufferHandle counterBuffer;
        }

        class Pass1Data
        {
            public RTHandle lightClusterIndices;
            public BufferHandle lightList;
            public float clusterScale, clusterBias;
            public int tileSize;
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

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Clustered Light Culling"))
            {
                pass.Initialize(computeShader, 0, clusterWidth, clusterHeight, settings.ClusterDepth);
                pass.ReadTexture("_LightClusterIndicesWrite", lightClusterIndices);
                pass.WriteBuffer("_LightClusterListWrite", lightList);

                var counterBuffer = renderGraph.GetBuffer();
                pass.WriteBuffer("_LightCounter", counterBuffer);

                var data = pass.SetRenderFunction<Pass0Data>((command, context, pass, data) =>
                {
                    command.SetBufferData(data.counterBuffer, zeroArray);
                    pass.SetInt(command, "_TileSize", data.tileSize);
                    pass.SetFloat(command, "_RcpClusterDepth", data.rcpClusterDepth);
                });

                data.tileSize = settings.TileSize;
                data.rcpClusterDepth = 1.0f / settings.ClusterDepth;
                data.counterBuffer = counterBuffer;
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Clustered Light Set Globals"))
            {
                var data = pass.SetRenderFunction<Pass1Data>((command, context, pass, data) =>
                {
                    // TODO: Handle this with proper pass inputs/outputs
                    command.SetGlobalTexture("_LightClusterIndices", data.lightClusterIndices);
                    command.SetGlobalBuffer("_LightClusterList", data.lightList);
                    command.SetGlobalFloat("_ClusterScale", data.clusterScale);
                    command.SetGlobalFloat("_ClusterBias", data.clusterBias);
                    command.SetGlobalInt("_TileSize", data.tileSize);
                });

                data.lightClusterIndices = lightClusterIndices;
                data.lightList = lightList;
                data.clusterScale = clusterScale;
                data.clusterBias = clusterBias;
                data.tileSize = settings.TileSize;
            }
        }
    }
}