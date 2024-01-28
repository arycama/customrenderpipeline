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

        private class Pass0Data
        {
            internal float nonLinearDepth;
            internal float volumeWidth;
            internal float volumeHeight;
            internal float volumeSlices;
            internal float volumeDepth;
            internal float blurSigma;
            internal float volumeTileSize;
            internal ClusteredLightCulling.Result clusteredLightCullingResult;
            internal int pointLightCount;
            internal int directionalLightCount;
            internal LightingSetup.Result lightingSetupResult;
        }

        public RTHandle Render(int pixelWidth, int pixelHeight, float farClipPlane, Camera camera, ClusteredLightCulling.Result clusteredLightCullingResult, LightingSetup.Result lightingSetupResult)
        {
            var width = Mathf.CeilToInt(pixelWidth / (float)settings.TileSize);
            var height = Mathf.CeilToInt(pixelHeight / (float)settings.TileSize);
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

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Volumetric Lighting"))
            {
                pass.Initialize(computeShader, 0, width, height, depth);
                pass.WriteTexture("_Result", volumetricLightingCurrent);

                pass.ReadTexture("_Input", volumetricLightingHistory);
                pass.ReadTexture("_LightClusterIndices", clusteredLightCullingResult.lightClusterIndices);
                pass.ReadTexture("_DirectionalShadows", lightingSetupResult.directionalShadows);
                pass.ReadTexture("_PointShadows", lightingSetupResult.pointShadows);

                pass.ReadBuffer("_LightClusterList", clusteredLightCullingResult.lightList);
                pass.ReadBuffer("_DirectionalMatrices", lightingSetupResult.directionalMatrices);
                pass.ReadBuffer("_DirectionalLights", lightingSetupResult.directionalLights);
                pass.ReadBuffer("_PointLights", lightingSetupResult.pointLights);

                var data = pass.SetRenderFunction<Pass0Data>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_NonLinearDepth", data.nonLinearDepth);
                    pass.SetFloat(command, "_VolumeWidth", data.volumeWidth);
                    pass.SetFloat(command, "_VolumeHeight", data.volumeHeight);
                    pass.SetFloat(command, "_VolumeSlices", data.volumeSlices);
                    pass.SetFloat(command, "_VolumeDepth", data.volumeDepth);
                    pass.SetFloat(command, "_BlurSigma", data.blurSigma);
                    pass.SetFloat(command, "_VolumeTileSize", data.volumeTileSize);

                    pass.SetFloat(command, "_ClusterScale", data.clusteredLightCullingResult.clusterScale);
                    pass.SetFloat(command, "_ClusterBias", data.clusteredLightCullingResult.clusterBias);
                    pass.SetInt(command, "_TileSize", data.clusteredLightCullingResult.tileSize);

                    pass.SetInt(command, "_DirectionalLightCount", data.lightingSetupResult.directionalLightCount);
                    pass.SetInt(command, "_PointLightCount", data.lightingSetupResult.pointLightCount);
                });

                data.nonLinearDepth = settings.NonLinearDepth ? 1.0f : 0.0f;
                data.volumeWidth = width;
                data.volumeHeight = height;
                data.volumeSlices = depth;
                data.volumeDepth = farClipPlane;
                data.blurSigma = settings.BlurSigma;
                data.volumeTileSize = settings.TileSize;
                data.clusteredLightCullingResult = clusteredLightCullingResult;
                data.lightingSetupResult = lightingSetupResult;
            }

            // Filter X
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Volumetric Lighting"))
            {
                pass.Initialize(computeShader, 1, width, height, depth);
                pass.ReadTexture("_Input", volumetricLightingCurrent);
                pass.WriteTexture("_Result", volumetricLighting);
            }

            // Filter Y
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Volumetric Lighting"))
            {
                pass.Initialize(computeShader, 2, width, height, depth);
                pass.ReadTexture("_Input", volumetricLighting);
                pass.WriteTexture("_Result", volumetricLightingHistory);
            }

            // Accumulate
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Volumetric Lighting"))
            {
                pass.Initialize(computeShader, 3, width, height, depth);
                pass.ReadTexture("_Input", volumetricLightingHistory);
                pass.WriteTexture("_Result", volumetricLighting);
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Volumetric Lighting"))
            {
                var data = pass.SetRenderFunction<Pass1Data>((command, context, pass, data) =>
                {
                    command.SetGlobalFloat("_NonLinearDepth", data.nonLinearDepth);
                    command.SetGlobalFloat("_VolumeWidth", data.volumeWidth);
                    command.SetGlobalFloat("_VolumeHeight", data.volumeHeight);
                    command.SetGlobalFloat("_VolumeSlices", data.volumeSlices);
                    command.SetGlobalFloat("_VolumeDepth", data.volumeDepth);
                });

                data.nonLinearDepth = settings.NonLinearDepth ? 1.0f : 0.0f;
                data.volumeWidth = width;
                data.volumeHeight = height;
                data.volumeSlices = depth;
                data.volumeDepth = farClipPlane;
            }

            return volumetricLighting;
        }

        private class Pass1Data
        {
            internal float nonLinearDepth;
            internal float volumeWidth;
            internal float volumeHeight;
            internal float volumeSlices;
            internal float volumeDepth;
        }
    }
}