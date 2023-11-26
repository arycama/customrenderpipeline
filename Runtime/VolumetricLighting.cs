using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

public class VolumetricLighting
{
    [Serializable]
    public class Settings
    {
        [SerializeField] private int tileSize = 8;
        [SerializeField] private int depthSlices = 128;
        [SerializeField, Range(0.0f, 2.0f)] private float blurSigma = 1.0f;
        [SerializeField] private bool nonLinearDepth = true;

        public int TileSize => tileSize;
        public int DepthSlices => depthSlices;
        public float BlurSigma => blurSigma;
        public bool NonLinearDepth => nonLinearDepth;
    }

    private static readonly int volumetricLightingId = Shader.PropertyToID("_VolumetricLighting");

    private Settings settings;
    private CameraTextureCache volumetricLightingTextureCache = new();

    public VolumetricLighting(Settings settings)
    {
        this.settings = settings;
    }

    public void Release()
    {
        volumetricLightingTextureCache.Dispose();
    }

    public class PassData
    {
        public TextureHandle lightClusterIndices;
    }

    public void Render(RenderGraph renderGraph, Camera camera, int frameCount, TextureHandle lightClusterIndices)
    {
        using var builder = renderGraph.AddRenderPass<PassData>("Volumetric Lighting", out var passData);
        passData.lightClusterIndices = builder.ReadTexture(lightClusterIndices);

        var width = Mathf.CeilToInt(camera.pixelWidth / (float)settings.TileSize);
        var height = Mathf.CeilToInt(camera.pixelHeight / (float)settings.TileSize);
        var depth = settings.DepthSlices;
        var volumetricLightingDescriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf)
        {
            dimension = TextureDimension.Tex3D,
            enableRandomWrite = true,
            volumeDepth = depth,
        };

        volumetricLightingTextureCache.GetTexture(camera, volumetricLightingDescriptor, out var volumetricLightingCurrent, out var volumetricLightingHistory, frameCount);

        builder.SetRenderFunc<PassData>((data, context) =>
        {
            var computeShader = Resources.Load<ComputeShader>("VolumetricLighting");
            context.cmd.SetGlobalFloat("_VolumeWidth", width);
            context.cmd.SetGlobalFloat("_VolumeHeight", height);
            context.cmd.SetGlobalFloat("_VolumeSlices", depth);
            context.cmd.SetGlobalFloat("_VolumeDepth", camera.farClipPlane);
            context.cmd.SetGlobalFloat("_NonLinearDepth", settings.NonLinearDepth ? 1.0f : 0.0f);
            context.cmd.SetComputeFloatParam(computeShader, "_BlurSigma", settings.BlurSigma);
            context.cmd.SetComputeIntParam(computeShader, "_VolumeTileSize", settings.TileSize);

            context.cmd.SetComputeTextureParam(computeShader, 0, "_LightClusterIndices", data.lightClusterIndices);
            context.cmd.SetComputeTextureParam(computeShader, 0, "_Input", volumetricLightingHistory);
            context.cmd.SetComputeTextureParam(computeShader, 0, "_Result", volumetricLightingCurrent);
            context.cmd.DispatchNormalized(computeShader, 0, width, height, depth);
            context.cmd.GetTemporaryRT(volumetricLightingId, volumetricLightingDescriptor);

            // Filter X
            context.cmd.SetComputeTextureParam(computeShader, 1, "_Input", volumetricLightingCurrent);
            context.cmd.SetComputeTextureParam(computeShader, 1, "_Result", volumetricLightingId);
            context.cmd.DispatchNormalized(computeShader, 1, width, height, depth);

            // Filter Y
            context.cmd.SetComputeTextureParam(computeShader, 2, "_Input", volumetricLightingId);
            context.cmd.SetComputeTextureParam(computeShader, 2, "_Result", volumetricLightingHistory);
            context.cmd.DispatchNormalized(computeShader, 2, width, height, depth);

            context.cmd.SetComputeTextureParam(computeShader, 3, "_Input", volumetricLightingHistory);
            context.cmd.SetComputeTextureParam(computeShader, 3, "_Result", volumetricLightingId);
            context.cmd.DispatchNormalized(computeShader, 3, width, height, 1);
            context.cmd.SetGlobalTexture("_VolumetricLighting", volumetricLightingId);
        });
    }

    public void CameraRenderComplete(CommandBuffer command)
    {
        command.ReleaseTemporaryRT(volumetricLightingId);
    }
}