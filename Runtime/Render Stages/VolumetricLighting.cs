using System;
using UnityEditor;
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

        public void Render(int screenWidth, int screenHeight, float farClipPlane, Camera camera, LightingSetup.Result lightingSetupResult, Texture2D blueNoise1D, Texture2D blueNoise2D, Matrix4x4 previousVpMatrix, Matrix4x4 invVpMatrix, Vector2 jitter)
        {
            var volumeWidth = Mathf.CeilToInt(screenWidth / (float)settings.TileSize);
            var volumeHeight = Mathf.CeilToInt(screenHeight / (float)settings.TileSize);
            var volumeDepth = settings.DepthSlices;
            var textures = colorHistory.GetTextures(volumeWidth, volumeHeight, camera, false, volumeDepth);

            var computeShader = Resources.Load<ComputeShader>("VolumetricLighting");

            // Can allocate this later but will need to put the data in a cbuffer to avoid too much setting.
            var volumetricLight = renderGraph.GetTexture(volumeWidth, volumeHeight, GraphicsFormat.R16G16B16A16_SFloat, volumeDepth, TextureDimension.Tex3D);

            var result = new Result(volumetricLight, /*volumetricLight.Scale, */camera.nearClipPlane, /*volumetricLight.Limit, */settings.MaxDistance, new Vector2(1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight), settings.DepthSlices, settings.NonLinearDepth ? 1.0f : 0.0f);
            renderGraph.ResourceMap.SetRenderPassData(result);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Volumetric Lighting"))
            {
                pass.Initialize(computeShader, 0, volumeWidth, volumeHeight, volumeDepth);
                pass.WriteTexture("_Result", textures.current);

                pass.ReadTexture("_Input", textures.history);

                lightingSetupResult.SetInputs(pass);
                pass.AddRenderPassData<ClusteredLightCulling.Result>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<Result>();

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

                    data.lightingSetupResult.SetProperties(pass, command);

                    pass.SetTexture(command, "_BlueNoise1D", data.blueNoise1D);
                    pass.SetTexture(command, "_BlueNoise2D", data.blueNoise2D);

                    pass.SetVector(command, "_ViewPosition", camera.transform.position);
                    pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                    pass.SetMatrix(command, "_WorldToPreviousClip", data.previousVpMatrix);
                    pass.SetMatrix(command, "_ClipToWorld", data.invVpMatrix);
                });

                data.nonLinearDepth = settings.NonLinearDepth ? 1.0f : 0.0f;
                data.volumeWidth = volumeWidth;
                data.volumeHeight = volumeHeight;
                data.volumeSlices = volumeDepth;
                data.volumeDepth = farClipPlane;
                data.blurSigma = settings.BlurSigma;
                data.volumeTileSize = settings.TileSize;
                data.lightingSetupResult = lightingSetupResult;
                data.blueNoise1D = blueNoise1D;
                data.blueNoise2D = blueNoise2D;
                data.scaledResolution = new Vector4(screenWidth, screenHeight, 1.0f / screenWidth, 1.0f / screenHeight);
                data.previousVpMatrix = previousVpMatrix;
                data.invVpMatrix = invVpMatrix;
                data.near = camera.nearClipPlane;
                data.far = camera.farClipPlane;
            }

            // Filter X
            var filterX = renderGraph.GetTexture(volumeWidth, volumeHeight, GraphicsFormat.R16G16B16A16_SFloat, volumeDepth, TextureDimension.Tex3D);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Filter X"))
            {
                pass.Initialize(computeShader, 1, volumeWidth, volumeHeight, volumeDepth);
                pass.ReadTexture("_Input", textures.current);
                pass.WriteTexture("_Result", filterX);
            }

            // Filter Y
            var filterY = renderGraph.GetTexture(volumeWidth, volumeHeight, GraphicsFormat.R16G16B16A16_SFloat, volumeDepth, TextureDimension.Tex3D);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Filter Y"))
            {
                pass.Initialize(computeShader, 2, volumeWidth, volumeHeight, volumeDepth);
                pass.ReadTexture("_Input", filterX);
                pass.WriteTexture("_Result", filterY);
            }

            // Accumulate
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Accumulate"))
            {
                pass.Initialize(computeShader, 3, volumeWidth, volumeHeight, volumeDepth);
                pass.ReadTexture("_Input", filterY);
                pass.WriteTexture("_Result", volumetricLight);
                pass.AddRenderPassData<Result>();

                var data = pass.SetRenderFunction<Pass0Data>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_NonLinearDepth", data.nonLinearDepth);
                    pass.SetFloat(command, "_VolumeWidth", data.volumeWidth);
                    pass.SetFloat(command, "_VolumeHeight", data.volumeHeight);
                    pass.SetFloat(command, "_VolumeSlices", data.volumeSlices);
                    pass.SetVector(command, "_ScaledResolution", data.scaledResolution);

                    pass.SetFloat(command, "_Near", data.near);
                    pass.SetFloat(command, "_Far", data.far);

                    pass.SetMatrix(command, "_PixelToWorldViewDir", Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(camera.pixelWidth, camera.pixelHeight, jitter, camera.fieldOfView, camera.aspect, Matrix4x4.Rotate(camera.transform.rotation)));
                });

                data.nonLinearDepth = settings.NonLinearDepth ? 1.0f : 0.0f;
                data.volumeWidth = volumeWidth;
                data.volumeHeight = volumeHeight;
                data.volumeSlices = volumeDepth;
                data.volumeDepth = farClipPlane;
                data.blurSigma = settings.BlurSigma;
                data.volumeTileSize = settings.TileSize;
                data.lightingSetupResult = lightingSetupResult;
                data.blueNoise1D = blueNoise1D;
                data.blueNoise2D = blueNoise2D;
                data.scaledResolution = new Vector4(screenWidth, screenHeight, 1.0f / screenWidth, 1.0f / screenHeight);
                data.previousVpMatrix = previousVpMatrix;
                data.invVpMatrix = invVpMatrix;
                data.near = camera.nearClipPlane;
                data.far = camera.farClipPlane;
            }

        }

        [Serializable]
        public class Settings
        {
            [field: SerializeField] public int TileSize { get; private set; } = 8;
            [field: SerializeField] public int DepthSlices { get; private set; } = 128;
            [field: SerializeField, Range(0.0f, 2.0f)] public float BlurSigma { get; private set; } = 1.0f;
            [field: SerializeField] public bool NonLinearDepth { get; private set; } = false;
            [field: SerializeField] public float MaxDistance { get; private set; } = 512.0f;
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
            internal LightingSetup.Result lightingSetupResult;
            internal Texture2D blueNoise1D;
            internal Texture2D blueNoise2D;
            internal Vector4 scaledResolution;
            internal Matrix4x4 previousVpMatrix;
            internal Matrix4x4 invVpMatrix;
            internal float near;
            internal float far;
        }

        public struct Result : IRenderPassData
        {
            private readonly RTHandle volumetricLighting;

            //private readonly Vector3 volumetricLightScale;
            private readonly float volumetricLightNear;

            //private readonly Vector3 volumetricLightMax;
            private readonly float volumetricLightFar;

            private readonly Vector2 rcpVolumetricLightResolution;
            private readonly float volumeSlices;
            private readonly float nonLinearDepth;

            public Result(RTHandle volumetricLighting, /*Vector3 volumetricLightScale,*/ float volumetricLightNear, /*Vector3 volumetricLightMax, */float volumetricLightFar, Vector2 rcpVolumetricLightResolution, float volumeSlices, float nonLinearDepth)
            {
                this.volumetricLighting = volumetricLighting;
                //this.volumetricLightScale = volumetricLightScale;
                this.volumetricLightNear = volumetricLightNear;
                //this.volumetricLightMax = volumetricLightMax;
                this.volumetricLightFar = volumetricLightFar;
                this.rcpVolumetricLightResolution = rcpVolumetricLightResolution;
                this.volumeSlices = volumeSlices;
                this.nonLinearDepth = nonLinearDepth;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_VolumetricLighting", volumetricLighting);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector(command, "_VolumetricLightScale", volumetricLighting.Scale);
                pass.SetFloat(command, "_VolumetricLightNear", volumetricLightNear);

                pass.SetVector(command, "_VolumetricLightMax", volumetricLighting.Limit);
                pass.SetFloat(command, "_VolumetricLightFar", volumetricLightFar);

                pass.SetVector(command, "_RcpVolumetricLightResolution", rcpVolumetricLightResolution);
                pass.SetFloat(command, "_VolumeSlices", volumeSlices);
                pass.SetFloat(command, "_NonLinearDepth", nonLinearDepth);
            }
        }
    }
}