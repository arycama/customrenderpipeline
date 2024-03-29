﻿using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class VolumetricLighting : RenderFeature
    {
        private readonly Settings settings;
        private readonly PersistentRTHandleCache colorHistory;

        public VolumetricLighting(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            colorHistory = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Volumetric Lighting", TextureDimension.Tex3D);
        }

        public Result Render(int screenWidth, int screenHeight, float farClipPlane, Camera camera, ClusteredLightCulling.Result clusteredLightCullingResult, LightingSetup.Result lightingSetupResult, Texture2D blueNoise1D, Texture2D blueNoise2D, Color fogColor, float fogStartDistance, float fogEndDistance, float fogDensity, float fogMode, Matrix4x4 previousVpMatrix, Matrix4x4 invVpMatrix, IRenderPassData commonData)
        {
            var width = Mathf.CeilToInt(screenWidth / (float)settings.TileSize);
            var height = Mathf.CeilToInt(screenHeight / (float)settings.TileSize);
            var depth = settings.DepthSlices;
            var textures = colorHistory.GetTextures(width, height, camera, false, depth);

            var computeShader = Resources.Load<ComputeShader>("VolumetricLighting");

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Volumetric Lighting"))
            {
                pass.Initialize(computeShader, 0, width, height, depth);
                pass.WriteTexture("_Result", textures.current);

                pass.ReadTexture("_Input", textures.history);

                clusteredLightCullingResult.SetInputs(pass);
                lightingSetupResult.SetInputs(pass);
                commonData.SetInputs(pass);
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();

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
            var filterX = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, depth, TextureDimension.Tex3D);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Filter X"))
            {
                pass.Initialize(computeShader, 1, width, height, depth);
                pass.ReadTexture("_Input", textures.current);
                pass.WriteTexture("_Result", filterX);
            }

            // Filter Y
            var filterY = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, depth, TextureDimension.Tex3D);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Filter Y"))
            {
                pass.Initialize(computeShader, 2, width, height, depth);
                pass.ReadTexture("_Input", filterX);
                pass.WriteTexture("_Result", filterY);
            }

            // Accumulate
            var result = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, depth, TextureDimension.Tex3D);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Accumulate"))
            {
                pass.Initialize(computeShader, 3, width, height, depth);
                pass.ReadTexture("_Input", filterY);
                pass.WriteTexture("_Result", result);
            }

            return new Result(result, settings.NonLinearDepth ? 1.0f : 0.0f, width, height, depth, farClipPlane);
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