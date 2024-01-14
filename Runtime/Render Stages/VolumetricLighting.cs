using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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

        private readonly Settings settings;
        private readonly CameraTextureCache volumetricLightingTextureCache = new();

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
            var volumetricLightingId = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, true, depth, TextureDimension.Tex3D);

            var pass0 = renderGraph.AddRenderPass(new ComputeRenderPass(computeShader, 0, width, height, depth));
            pass0.SetRenderFunction((command, context) =>
            {
                using var profilerScope = command.BeginScopedSample("Volumetric Lighting");

                command.SetGlobalFloat("_VolumeWidth", width);
                command.SetGlobalFloat("_VolumeHeight", height);
                command.SetGlobalFloat("_VolumeSlices", depth);
                command.SetGlobalFloat("_VolumeDepth", camera.farClipPlane);
                command.SetGlobalFloat("_NonLinearDepth", settings.NonLinearDepth ? 1.0f : 0.0f);
                pass0.SetFloat(command, "_BlurSigma", settings.BlurSigma);
                pass0.SetFloat(command, "_VolumeTileSize", settings.TileSize);

                pass0.SetTexture(command, "_Input", volumetricLightingHistory);
                pass0.SetTexture(command, "_Result", volumetricLightingCurrent);
                pass0.Execute(command);
            });

            // Filter X
            var pass1 = renderGraph.AddRenderPass(new ComputeRenderPass(computeShader, 1, width, height, depth));
            pass1.SetRenderFunction((command, context) =>
            {
                pass1.SetTexture(command, "_Input", volumetricLightingCurrent);
                pass1.SetTexture(command, "_Result", volumetricLightingId);
                pass1.Execute(command);
            });

            // Filter Y
            var pass2 = renderGraph.AddRenderPass(new ComputeRenderPass(computeShader, 2, width, height, depth));
            pass2.SetRenderFunction((command, context) =>
            {
                pass2.SetTexture(command, "_Input", volumetricLightingId);
                pass2.SetTexture(command, "_Result", volumetricLightingHistory);
                pass2.Execute(command);
            });

            // Accumulate
            var pass3 = renderGraph.AddRenderPass(new ComputeRenderPass(computeShader, 3, width, height, depth));
            pass3.SetRenderFunction((command, context) =>
            {
                pass3.SetTexture(command, "_Input", volumetricLightingHistory);
                pass3.SetTexture(command, "_Result", volumetricLightingId);
                pass3.Execute(command);
                command.SetGlobalTexture("_VolumetricLighting", volumetricLightingId);
            });
        }
    }
}