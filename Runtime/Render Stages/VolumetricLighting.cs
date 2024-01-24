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
        private readonly CameraTextureCache volumetricLightingTextureCache;

        public VolumetricLighting(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            volumetricLightingTextureCache = new(renderGraph, "Volumetric Lighting");
        }

        public void Release()
        {
            volumetricLightingTextureCache.Dispose();
        }

        public RTHandle Render(int pixelWidth, int pixelHeight, float farClipPlane, Camera camera)
        {
            var scaledWidth = pixelWidth;
            var scaledHeight = pixelHeight;

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
            var volumetricLighting = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, true, depth, TextureDimension.Tex3D);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>())
            {
                pass.Initialize(computeShader, 0, width, height, depth);
                pass.ReadTexture("_Input", volumetricLightingHistory);
                pass.WriteTexture("_Result", volumetricLightingCurrent);

                pass.SetRenderFunction((command, context) =>
                {
                    pass.SetFloat(command, "_NonLinearDepth", settings.NonLinearDepth ? 1.0f : 0.0f);
                    pass.SetFloat(command, "_VolumeWidth", width);
                    pass.SetFloat(command, "_VolumeHeight", height);
                    pass.SetFloat(command, "_VolumeSlices", depth);
                    pass.SetFloat(command, "_VolumeDepth", farClipPlane);
                    pass.SetFloat(command, "_BlurSigma", settings.BlurSigma);
                    pass.SetFloat(command, "_VolumeTileSize", settings.TileSize);
                });
            }

            // Filter X
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>())
            {
                pass.Initialize(computeShader, 1, width, height, depth);
                pass.ReadTexture("_Input", volumetricLightingCurrent);
                pass.WriteTexture("_Result", volumetricLighting);
            }

            // Filter Y
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>())
            {
                pass.Initialize(computeShader, 2, width, height, depth);
                pass.ReadTexture("_Input", volumetricLighting);
                pass.WriteTexture("_Result", volumetricLightingHistory);
            }

            // Accumulate
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>())
            {
                pass.Initialize(computeShader, 3, width, height, depth);
                pass.ReadTexture("_Input", volumetricLightingHistory);
                pass.WriteTexture("_Result", volumetricLighting);
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>())
            {
                pass.SetRenderFunction((command, context) =>
                {
                    command.SetGlobalFloat("_NonLinearDepth", settings.NonLinearDepth ? 1.0f : 0.0f);
                    command.SetGlobalFloat("_VolumeWidth", width);
                    command.SetGlobalFloat("_VolumeHeight", height);
                    command.SetGlobalFloat("_VolumeSlices", depth);
                    command.SetGlobalFloat("_VolumeDepth", farClipPlane);
                });
            }

            return volumetricLighting;
        }
    }
}