using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class VolumetricLighting : RenderFeature
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

        public VolumetricLighting(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
        }

        public void Release()
        {
            volumetricLightingTextureCache.Dispose();
        }

        public void Render(Camera camera, float scale)
        {
            var scaledWidth = (int)(camera.pixelWidth * scale);
            var scaledHeight = (int)(camera.pixelHeight * scale);

            var width = Mathf.CeilToInt(scaledWidth / (float)settings.TileSize);
            var height = Mathf.CeilToInt(scaledHeight / (float)settings.TileSize);
            var depth = settings.DepthSlices;
            var volumetricLightingDescriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGBHalf)
            {
                dimension = TextureDimension.Tex3D,
                enableRandomWrite = true,
                volumeDepth = depth,
            };

            volumetricLightingTextureCache.GetTexture(camera, volumetricLightingDescriptor, out var volumetricLightingCurrent, out var volumetricLightingHistory);

            var computeShader = Resources.Load<ComputeShader>("VolumetricLighting");

            renderGraph.AddRenderPass((command, context) =>
            {
                using var profilerScope = command.BeginScopedSample("Volumetric Lighting");

                command.SetGlobalFloat("_VolumeWidth", width);
                command.SetGlobalFloat("_VolumeHeight", height);
                command.SetGlobalFloat("_VolumeSlices", depth);
                command.SetGlobalFloat("_VolumeDepth", camera.farClipPlane);
                command.SetGlobalFloat("_NonLinearDepth", settings.NonLinearDepth ? 1.0f : 0.0f);
                command.SetComputeFloatParam(computeShader, "_BlurSigma", settings.BlurSigma);
                command.SetComputeIntParam(computeShader, "_VolumeTileSize", settings.TileSize);

                command.SetComputeTextureParam(computeShader, 0, "_Input", volumetricLightingHistory);
                command.SetComputeTextureParam(computeShader, 0, "_Result", volumetricLightingCurrent);
                command.DispatchNormalized(computeShader, 0, width, height, depth);
                command.GetTemporaryRT(volumetricLightingId, volumetricLightingDescriptor);

                // Filter X
                command.SetComputeTextureParam(computeShader, 1, "_Input", volumetricLightingCurrent);
                command.SetComputeTextureParam(computeShader, 1, "_Result", volumetricLightingId);
                command.DispatchNormalized(computeShader, 1, width, height, depth);

                // Filter Y
                command.SetComputeTextureParam(computeShader, 2, "_Input", volumetricLightingId);
                command.SetComputeTextureParam(computeShader, 2, "_Result", volumetricLightingHistory);
                command.DispatchNormalized(computeShader, 2, width, height, depth);

                command.SetComputeTextureParam(computeShader, 3, "_Input", volumetricLightingHistory);
                command.SetComputeTextureParam(computeShader, 3, "_Result", volumetricLightingId);
                command.DispatchNormalized(computeShader, 3, width, height, 1);
                command.SetGlobalTexture("_VolumetricLighting", volumetricLightingId);
            });
        }
    }
}