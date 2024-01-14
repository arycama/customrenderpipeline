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

        private Settings settings;
        private GraphicsBuffer counterBuffer;
        private GraphicsBuffer lightList;

        private static readonly uint[] zeroArray = new uint[1] { 0 };

        private int DivRoundUp(int x, int y) => (x + y - 1) / y;

        public ClusteredLightCulling(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            counterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint)) { name = nameof(counterBuffer) };
        }

        public void Release()
        {
            lightList?.Release();
            counterBuffer.Release();
        }

        public void Render(Camera camera, float scale)
        {
            var scaledWidth = (int)(camera.pixelWidth * scale);
            var scaledHeight = (int)(camera.pixelHeight * scale);

            var clusterWidth = DivRoundUp(scaledWidth, settings.TileSize);
            var clusterHeight = DivRoundUp(scaledHeight, settings.TileSize);
            var clusterCount = clusterWidth * clusterHeight * settings.ClusterDepth;

            GraphicsUtilities.SafeExpand(ref lightList, clusterCount * settings.MaxLightsPerTile);

            var clusterScale = settings.ClusterDepth / Mathf.Log(camera.farClipPlane / camera.nearClipPlane, 2f);
            var clusterBias = -(settings.ClusterDepth * Mathf.Log(camera.nearClipPlane, 2f) / Mathf.Log(camera.farClipPlane / camera.nearClipPlane, 2f));

            var computeShader = Resources.Load<ComputeShader>("ClusteredLightCulling");
            var lightClusterIndicesId = renderGraph.GetTexture(clusterWidth, clusterHeight, GraphicsFormat.R32G32_SInt, true, settings.ClusterDepth, TextureDimension.Tex3D);

            var pass = renderGraph.AddRenderPass<ComputeRenderPass>(new (computeShader, 0));
            pass.ReadTexture("_LightClusterIndicesWrite", lightClusterIndicesId);

            pass.SetRenderFunction((command, context) =>
            {
                command.SetBufferData(counterBuffer, zeroArray);
                command.SetComputeBufferParam(computeShader, 0, "_LightCounter", counterBuffer);
                command.SetComputeBufferParam(computeShader, 0, "_LightClusterListWrite", lightList);
                pass.SetInt(command, "_TileSize", settings.TileSize);
                pass.SetFloat(command, "_RcpClusterDepth", 1f / settings.ClusterDepth);
                command.DispatchNormalized(computeShader, 0, clusterWidth, clusterHeight, settings.ClusterDepth);

                command.SetGlobalTexture("_LightClusterIndices", lightClusterIndicesId);
                command.SetGlobalBuffer("_LightClusterList", lightList);
                command.SetGlobalFloat("_ClusterScale", clusterScale);
                command.SetGlobalFloat("_ClusterBias", clusterBias);
                command.SetGlobalInt("_TileSize", settings.TileSize);
            });
        }
    }
}