using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class VolumetricLighting : RenderFeature
    {

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

        public Result Render(int screenWidth, int screenHeight, float farClipPlane, Camera camera, ClusteredLightCulling.Result clusteredLightCullingResult, LightingSetup.Result lightingSetupResult, BufferHandle exposureBuffer, Texture2D blueNoise1D, Texture2D blueNoise2D, Color fogColor, float fogStartDistance, float fogEndDistance, float fogDensity, float fogMode, Matrix4x4 previousVpMatrix, Matrix4x4 invVpMatrix, IRenderPassData commonData)
        {
            var width = Mathf.CeilToInt(screenWidth / (float)settings.TileSize);
            var height = Mathf.CeilToInt(screenHeight / (float)settings.TileSize);
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

                clusteredLightCullingResult.SetInputs(pass);
                lightingSetupResult.SetInputs(pass);
                commonData.SetInputs(pass);

                var data = pass.SetRenderFunction<Pass0Data>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_NonLinearDepth", data.nonLinearDepth);
                    pass.SetFloat(command, "_VolumeWidth", data.volumeWidth);
                    pass.SetFloat(command, "_VolumeHeight", data.volumeHeight);
                    pass.SetFloat(command, "_VolumeSlices", data.volumeSlices);
                    pass.SetFloat(command, "_BlurSigma", data.blurSigma);
                    pass.SetFloat(command, "_VolumeTileSize", data.volumeTileSize);
                    pass.SetFloat(command, "_VolumeTileSize", data.volumeTileSize);

                    pass.SetFloat(command, "_Near", data.near);
                    pass.SetFloat(command, "_Far", data.far);

                    data.clusteredLightCullingResult.SetProperties(pass, command);
                    data.lightingSetupResult.SetProperties(pass, command);

                    pass.SetTexture(command, "_BlueNoise1D", data.blueNoise1D);
                    pass.SetTexture(command, "_BlueNoise2D", data.blueNoise2D);

                    pass.SetConstantBuffer(command, "Exposure", data.exposureBuffer);

                    pass.SetVector(command, "_FogColor", data.fogColor);
                    pass.SetFloat(command, "_FogStartDistance", data.fogStartDistance);
                    pass.SetFloat(command, "_FogEndDistance", data.fogEndDistance);
                    pass.SetFloat(command, "_FogDensity", data.fogDensity);
                    pass.SetFloat(command, "_FogMode", data.fogMode);

                    pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                    pass.SetMatrix(command, "_WorldToPreviousClip", data.previousVpMatrix);
                    pass.SetMatrix(command, "_ClipToWorld", data.invVpMatrix);

                    commonData.SetProperties(pass, command);
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
                data.exposureBuffer = exposureBuffer;
                data.blueNoise1D = blueNoise1D;
                data.blueNoise2D = blueNoise2D;
                data.fogColor = fogColor;
                data.fogStartDistance = fogStartDistance;
                data.fogEndDistance = fogEndDistance;
                data.fogDensity = fogDensity;
                data.fogMode = fogMode;
                data.scaledResolution = new Vector4(screenWidth, screenHeight, 1.0f / screenWidth, 1.0f / screenHeight);
                data.previousVpMatrix = previousVpMatrix;
                data.invVpMatrix = invVpMatrix;
                data.near = camera.nearClipPlane;
                data.far = camera.farClipPlane;
            }

            // Filter X
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Filter X"))
            {
                pass.Initialize(computeShader, 1, width, height, depth);
                pass.ReadTexture("_Input", volumetricLightingCurrent);
                pass.WriteTexture("_Result", volumetricLighting);
            }

            // Filter Y
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Filter Y"))
            {
                pass.Initialize(computeShader, 2, width, height, depth);
                pass.ReadTexture("_Input", volumetricLighting);
                pass.WriteTexture("_Result", volumetricLightingHistory);
            }

            // Accumulate
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Accumulate"))
            {
                pass.Initialize(computeShader, 3, width, height, depth);
                pass.ReadTexture("_Input", volumetricLightingHistory);
                pass.WriteTexture("_Result", volumetricLighting);
            }

            return new Result(volumetricLighting, settings.NonLinearDepth ? 1.0f : 0.0f, width, height, depth, farClipPlane);
        }

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
            internal BufferHandle exposureBuffer;
            internal Texture2D blueNoise1D;
            internal Texture2D blueNoise2D;
            internal Color fogColor;
            internal float fogStartDistance;
            internal float fogEndDistance;
            internal float fogDensity;
            internal float fogMode;
            internal Vector4 scaledResolution;
            internal Matrix4x4 previousVpMatrix;
            internal Matrix4x4 invVpMatrix;
            internal float near;
            internal float far;
        }

        public struct Result
        {
            private readonly RTHandle volumetricLighting;
            private readonly float nonLinearDepth, volumeWidth, volumeHeight, volumeSlices, volumeDepth;

            public Result(RTHandle volumetricLighting, float nonLinearDepth, float volumeWidth, float volumeHeight, float volumeSlices, float volumeDepth)
            {
                this.volumetricLighting = volumetricLighting ?? throw new ArgumentNullException(nameof(volumetricLighting));
                this.nonLinearDepth = nonLinearDepth;
                this.volumeWidth = volumeWidth;
                this.volumeHeight = volumeHeight;
                this.volumeSlices = volumeSlices;
                this.volumeDepth = volumeDepth;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_VolumetricLighting", volumetricLighting);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetFloat(command, "_NonLinearDepth", nonLinearDepth);
                pass.SetFloat(command, "_VolumeWidth", volumeWidth);
                pass.SetFloat(command, "_VolumeHeight", volumeHeight);
                pass.SetFloat(command, "_VolumeSlices", volumeSlices);
                pass.SetFloat(command, "_VolumeDepth", volumeDepth);
            }
        }
    }
}