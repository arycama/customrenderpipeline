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

        class PassData0
        {
            public ComputeRenderPass pass;
            public BufferHandle counterBuffer;
            public int tileSize;
            public float rcpClusterDepth;
        }

        class PassData1
        {
            public int tileSize;
            public RTHandle lightClusterIndices;
            public BufferHandle lightList;
            public float clusterScale;
            public float clusterBias;
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

            var pass0 = renderGraph.AddRenderPass<ComputeRenderPass>();
            pass0.Initialize(computeShader, 0, clusterWidth, clusterHeight, settings.ClusterDepth);
            pass0.ReadTexture("_LightClusterIndicesWrite", lightClusterIndices);

            var lightList = renderGraph.GetBuffer(clusterCount * settings.MaxLightsPerTile);
            pass0.WriteBuffer("_LightClusterListWrite", lightList);

            var counterBuffer = renderGraph.GetBuffer();
            pass0.WriteBuffer("_LightCounter", counterBuffer);

            var data0 = pass0.SetRenderFunction<PassData0>((command, context, data) =>
            {
                command.SetBufferData(data.counterBuffer, zeroArray);
                data.pass.SetInt(command, "_TileSize", data.tileSize);
                data.pass.SetFloat(command, "_RcpClusterDepth", data.rcpClusterDepth);
            });

            data0.pass = pass0;
            data0.counterBuffer = counterBuffer;
            data0.tileSize = settings.TileSize;
            data0.rcpClusterDepth = 1.0f / settings.ClusterDepth;
            
            var pass1 = renderGraph.AddRenderPass<GlobalRenderPass>();
            var data1 = pass1.SetRenderFunction<PassData1>((command, context, data) =>
            {
                // TODO: Handle this with proper pass inputs/outputs
                command.SetGlobalTexture("_LightClusterIndices", data.lightClusterIndices);
                command.SetGlobalBuffer("_LightClusterList", data.lightList);
                command.SetGlobalFloat("_ClusterScale", data.clusterScale);
                command.SetGlobalFloat("_ClusterBias", data.clusterBias);
                command.SetGlobalInt("_TileSize", data.tileSize);
            });

            data1.lightClusterIndices = lightClusterIndices;
            data1.lightList = lightList;
            data1.clusterScale = clusterScale;
            data1.clusterBias = clusterBias;
            data1.tileSize = settings.TileSize;
        }
    }
}