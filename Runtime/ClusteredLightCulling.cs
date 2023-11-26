using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class ClusteredLightCulling
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
    private static readonly uint[] zeroArray = new uint[1] { 0 };

    private int DivRoundUp(int x, int y) => (x + y - 1) / y;

    public ClusteredLightCulling(Settings settings)
    {
        this.settings = settings;
    }

    public class PassData
    {
        public int clusterWidth, clusterHeight;
        public ComputeBufferHandle lightList;
        public ComputeBufferHandle counterBuffer;
        public Camera camera;
        public Settings settings;
        public TextureHandle lightClusterIndices;
    }

    public void Render(RenderGraph renderGraph, Camera camera, out TextureHandle lightClusterIndices)
    {
        using var builder = renderGraph.AddRenderPass<PassData>("Clustered Light Culling", out var passData);
        passData.clusterWidth = DivRoundUp(camera.pixelWidth, settings.TileSize);
        passData.clusterHeight = DivRoundUp(camera.pixelHeight, settings.TileSize);

        lightClusterIndices = renderGraph.CreateTexture(new TextureDesc(passData.clusterWidth, passData.clusterHeight) { colorFormat = GraphicsFormat.R32G32_UInt, dimension = TextureDimension.Tex3D, enableRandomWrite = true, slices = settings.ClusterDepth });

        var clusterCount = passData.clusterWidth * passData.clusterHeight * settings.ClusterDepth;
        var lightList = renderGraph.CreateComputeBuffer(new ComputeBufferDesc(clusterCount * settings.MaxLightsPerTile, sizeof(int)));

        passData.lightList = builder.WriteComputeBuffer(lightList);
        passData.counterBuffer = builder.CreateTransientComputeBuffer(new ComputeBufferDesc(1, sizeof(uint)));
        passData.camera = camera;
        passData.settings = settings;
        passData.lightClusterIndices = builder.WriteTexture(lightClusterIndices);

        builder.SetRenderFunc<PassData>((data, context) =>
        {
            var clusterScale = data.settings.ClusterDepth / Mathf.Log(data.camera.farClipPlane / data.camera.nearClipPlane, 2f);
            var clusterBias = -(data.settings.ClusterDepth * Mathf.Log(data.camera.nearClipPlane, 2f) / Mathf.Log(data.camera.farClipPlane / data.camera.nearClipPlane, 2f));

            var computeShader = Resources.Load<ComputeShader>("ClusteredLightCulling");

            context.cmd.SetBufferData(data.counterBuffer, zeroArray);
            //command.SetComputeBufferParam(computeShader, 0, "_LightData", lightData);
            context.cmd.SetComputeBufferParam(computeShader, 0, "_LightCounter", data.counterBuffer);
            context.cmd.SetComputeBufferParam(computeShader, 0, "_LightClusterListWrite", data.lightList);
            context.cmd.SetComputeTextureParam(computeShader, 0, "_LightClusterIndicesWrite", data.lightClusterIndices);
            //command.SetComputeIntParam(computeShader, "_LightCount", lightData.Count);
            context.cmd.SetComputeIntParam(computeShader, "_TileSize", data.settings.TileSize);
            context.cmd.SetComputeFloatParam(computeShader, "_RcpClusterDepth", 1f / data.settings.ClusterDepth);
            context.cmd.DispatchNormalized(computeShader, 0, data.clusterWidth, data.clusterHeight, data.settings.ClusterDepth);

            context.cmd.SetGlobalTexture("_LightClusterIndices", data.lightClusterIndices);
            context.cmd.SetGlobalBuffer("_LightClusterList", data.lightList);
            context.cmd.SetGlobalFloat("_ClusterScale", clusterScale);
            context.cmd.SetGlobalFloat("_ClusterBias", clusterBias);
            context.cmd.SetGlobalInt("_TileSize", data.settings.TileSize);
        });
    }
}
