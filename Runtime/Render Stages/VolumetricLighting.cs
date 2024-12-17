using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class VolumetricLighting : RenderFeature<(int screenWidth, int screenHeight, float farClipPlane, Camera camera, Texture2D blueNoise1D, Texture2D blueNoise2D, Vector2 jitter)>
    {
        private readonly Settings settings;
        private readonly PersistentRTHandleCache colorHistory;

        public VolumetricLighting(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            colorHistory = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Volumetric Lighting", TextureDimension.Tex3D);
        }

        public override void Render((int screenWidth, int screenHeight, float farClipPlane, Camera camera, Texture2D blueNoise1D, Texture2D blueNoise2D, Vector2 jitter) data)
        {
            var volumeWidth = Mathf.CeilToInt(data.screenWidth / (float)settings.TileSize);
            var volumeHeight = Mathf.CeilToInt(data.screenHeight / (float)settings.TileSize);
            var volumeDepth = settings.DepthSlices;
            var textures = colorHistory.GetTextures(volumeWidth, volumeHeight, data.camera, false, volumeDepth);

            var computeShader = Resources.Load<ComputeShader>("VolumetricLighting");

            // Can allocate this later but will need to put the data in a cbuffer to avoid too much setting.
            var volumetricLight = renderGraph.GetTexture(volumeWidth, volumeHeight, GraphicsFormat.R16G16B16A16_SFloat, volumeDepth, TextureDimension.Tex3D);

            var result = new Result(volumetricLight, /*volumetricLight.Scale, */data.camera.nearClipPlane, /*volumetricLight.Limit, */settings.MaxDistance, new Vector2(1.0f / data.camera.pixelWidth, 1.0f / data.camera.pixelHeight), settings.DepthSlices, settings.NonLinearDepth ? 1.0f : 0.0f);
            renderGraph.SetResource(result);;

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Volumetric Lighting"))
            {
                pass.Initialize(computeShader, 0, volumeWidth, volumeHeight, volumeDepth);
                pass.WriteTexture("_Result", textures.current);

                pass.ReadTexture("_Input", textures.history);

                pass.AddRenderPassData<ClusteredLightCulling.Result>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<Result>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction(
                (
                    nonLinearDepth: settings.NonLinearDepth ? 1.0f : 0.0f,
                    volumeWidth: volumeWidth,
                    volumeHeight: volumeHeight,
                    volumeSlices: volumeDepth,
                    blurSigma: settings.BlurSigma,
                    volumeTileSize: settings.TileSize,
                    blueNoise1D: data.blueNoise1D,
                    blueNoise2D: data.blueNoise2D,
                    history: textures.history,
                    pixelToWorldViewDir: Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(volumeWidth, volumeHeight, data.jitter, data.camera.fieldOfView, data.camera.aspect, Matrix4x4.Rotate(data.camera.transform.rotation), false, true)
                ),
                (command, pass, data) =>
                {
                    pass.SetFloat("_NonLinearDepth", data.nonLinearDepth);
                    pass.SetFloat("_VolumeWidth", data.volumeWidth);
                    pass.SetFloat("_VolumeHeight", data.volumeHeight);
                    pass.SetFloat("_VolumeSlices", data.volumeSlices);
                    pass.SetFloat("_BlurSigma", data.blurSigma);
                    pass.SetFloat("_VolumeTileSize", data.volumeTileSize);

                    pass.SetTexture("_BlueNoise1D", data.blueNoise1D);
                    pass.SetTexture("_BlueNoise2D", data.blueNoise2D);

                    pass.SetVector("_InputScale", data.history.Scale);
                    pass.SetVector("_InputMax", data.history.Limit);
                    pass.SetMatrix("_PixelToWorldViewDir", data.pixelToWorldViewDir);
                });
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
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction(
                (
                    nonLinearDepth: settings.NonLinearDepth ? 1.0f : 0.0f,
                    volumeWidth: volumeWidth,
                    volumeHeight: volumeHeight,
                    volumeSlices: volumeDepth,
                    volumeDepth: data.farClipPlane,
                    blurSigma: settings.BlurSigma,
                    volumeTileSize: settings.TileSize,
                    blueNoise1D: data.blueNoise1D,
                    blueNoise2D: data.blueNoise2D,
                    volumeDistancePerSlice: settings.MaxDistance / settings.DepthSlices,
                    depthSlices: settings.DepthSlices,
                    pixelToWorldViewDir: Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(volumeWidth, volumeHeight, data.jitter, data.camera.fieldOfView, data.camera.aspect, Matrix4x4.Rotate(data.camera.transform.rotation), false, true)
                ),
                (command, pass, data) =>
                {
                    pass.SetFloat("_NonLinearDepth", data.nonLinearDepth);
                    pass.SetFloat("_VolumeWidth", data.volumeWidth);
                    pass.SetFloat("_VolumeHeight", data.volumeHeight);
                    pass.SetFloat("_VolumeSlices", data.volumeSlices);

                    pass.SetFloat("_VolumeDistancePerSlice", data.volumeDistancePerSlice);
                    pass.SetInt("_VolumeSlicesInt", data.depthSlices);

                    pass.SetMatrix("_PixelToWorldViewDir", data.pixelToWorldViewDir);
                });
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

        public readonly struct Result : IRenderPassData
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

            public readonly void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_VolumetricLighting", volumetricLighting);
            }

            public readonly void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector("_VolumetricLightScale", volumetricLighting.Scale);
                pass.SetFloat("_VolumetricLightNear", volumetricLightNear);

                pass.SetVector("_VolumetricLightMax", volumetricLighting.Limit);
                pass.SetFloat("_VolumetricLightFar", volumetricLightFar);

                pass.SetVector("_RcpVolumetricLightResolution", rcpVolumetricLightResolution);
                pass.SetFloat("_VolumeSlices", volumeSlices);
                pass.SetFloat("_NonLinearDepth", nonLinearDepth);
            }
        }
    }
}