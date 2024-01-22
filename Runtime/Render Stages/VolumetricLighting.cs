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

        class Pass0Data { }
        class Pass1Data { }
        class Pass2Data { }
        class Pass3Data { }

        public void Render(int pixelWidth, int pixelHeight, float farClipPlane, Camera camera)
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
            var volumetricLightingId = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, true, depth, TextureDimension.Tex3D);

            var pass0 = renderGraph.AddRenderPass<ComputeRenderPass>();
            pass0.Initialize(computeShader, 0, width, height, depth);
            var data0 = pass0.SetRenderFunction<Pass0Data>((command, context, data) =>
            {
                command.SetGlobalFloat("_VolumeWidth", width);
                command.SetGlobalFloat("_VolumeHeight", height);
                command.SetGlobalFloat("_VolumeSlices", depth);
                command.SetGlobalFloat("_VolumeDepth", farClipPlane);
                command.SetGlobalFloat("_NonLinearDepth", settings.NonLinearDepth ? 1.0f : 0.0f);
                pass0.SetFloat(command, "_BlurSigma", settings.BlurSigma);
                pass0.SetFloat(command, "_VolumeTileSize", settings.TileSize);

                pass0.SetTexture(command, "_Input", volumetricLightingHistory);
                pass0.SetTexture(command, "_Result", volumetricLightingCurrent);
            });

            // Filter X
            var pass1 = renderGraph.AddRenderPass<ComputeRenderPass>();
            pass1.Initialize(computeShader, 1, width, height, depth);
            var data1 = pass1.SetRenderFunction<Pass1Data>((command, context, data) =>
            {
                pass1.SetTexture(command, "_Input", volumetricLightingCurrent);
                pass1.SetTexture(command, "_Result", volumetricLightingId);
            });

            // Filter Y
            var pass2 = renderGraph.AddRenderPass<ComputeRenderPass>();
            pass2.Initialize(computeShader, 2, width, height, depth);
            var data2 = pass2.SetRenderFunction<Pass2Data>((command, context, data) =>
            {
                pass2.SetTexture(command, "_Input", volumetricLightingId);
                pass2.SetTexture(command, "_Result", volumetricLightingHistory);
            });

            // Accumulate
            var pass3 = renderGraph.AddRenderPass<ComputeRenderPass>();
            pass3.Initialize(computeShader, 3, width, height, depth);
            var data3 = pass3.SetRenderFunction<Pass3Data>((command, context, data) =>
            {
                pass3.SetTexture(command, "_Input", volumetricLightingHistory);
                pass3.SetTexture(command, "_Result", volumetricLightingId);
            });

            var pass4 = renderGraph.AddRenderPass<GlobalRenderPass>();
            pass4.SetRenderFunction((command, context) =>
            {
                command.SetGlobalTexture("_VolumetricLighting", volumetricLightingId);
            });
        }
    }
}