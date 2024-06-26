﻿using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class VolumetricClouds : RenderFeature
    {
        [Serializable]
        public class Settings
        {
            [field: Header("Weather Map")]
            [field: SerializeField] public Vector2Int WeatherMapResolution { get; private set; } = new(256, 256);
            [field: SerializeField] public float WeatherMapScale { get; private set; } = 32768.0f;
            [field: SerializeField] public Vector2 WeatherMapSpeed { get; private set; } = Vector2.zero;
            [field: SerializeField, Range(0.0f, 1.0f)] public float WeatherMapStrength { get; private set; } = 1.0f;
            [field: SerializeField] public FractalNoiseParameters WeatherMapNoiseParams { get; private set; }

            [field: Header("Noise Texture")]
            [field: SerializeField] public Vector3Int NoiseResolution { get; private set; } = new(128, 64, 128);
            [field: SerializeField] public float NoiseScale { get; private set; } = 4096.0f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float NoiseStrength { get; private set; } = 1.0f;
            [field: SerializeField] public FractalNoiseParameters NoiseParams { get; private set; }
            [field: SerializeField] public FractalNoiseParameters CellularNoiseParams { get; private set; }

            [field: Header("Detail Noise Texture")]
            [field: SerializeField] public Vector3Int DetailNoiseResolution { get; private set; } = new(32, 32, 32);
            [field: SerializeField] public float DetailScale { get; private set; } = 512.0f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float DetailStrength { get; private set; } = 1.0f;
            [field: SerializeField] public FractalNoiseParameters DetailNoiseParams { get; private set; }

            [field: Header("Cloud Shadows")]
            [field: SerializeField] public int ShadowResolution { get; private set; } = 1024;
            [field: SerializeField] public float ShadowRadius { get; private set; } = 150000.0f;
            [field: SerializeField, Range(1, 64)] public int ShadowSamples { get; private set; } = 24;

            [field: Header("Rendering")]
            [field: SerializeField, Range(0.0f, 0.1f)] public float Density { get; private set; } = 0.05f;
            [field: SerializeField] public float StartHeight { get; private set; } = 1024.0f;
            [field: SerializeField] public float LayerThickness { get; private set; } = 512.0f;
            [field: SerializeField] public int RaySamples { get; private set; } = 32;
            [field: SerializeField] public int LightSamples { get; private set; } = 5;
            [field: SerializeField] public float LightDistance { get; private set; } = 512.0f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float TransmittanceThreshold { get; private set; } = 0.05f;

            [field: SerializeField, Range(-1.0f, 1.0f)] public float BackScatterPhase { get; private set; } = -0.15f;
            [field: SerializeField, Range(0.0f, 10.0f)] public float BackScatterScale { get; private set; } = 2.16f;

            [field: SerializeField, Range(-1.0f, 1.0f)] public float ForwardScatterPhase { get; private set; } = 0.8f;
            [field: SerializeField, Range(0.0f, 10.0f)] public float ForwardScatterScale { get; private set; } = 1.0f;


            [field: Header("Temporal")]
            [field: SerializeField, Range(0.0f, 1.0f)] public float StationaryBlend { get; private set; } = 0.95f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float MotionBlend { get; private set; } = 0.0f;
            [field: SerializeField, Min(0.0f)] public float MotionFactor { get; private set; } = 6000.0f;

            [field: NonSerialized] public int Version { get; private set; }

            public void SetCloudPassData(CommandBuffer command, RenderPass pass)
            {
                pass.SetFloat(command, "_WeatherMapStrength", WeatherMapStrength);
                pass.SetFloat(command, "_WeatherMapScale", MathUtils.Rcp(WeatherMapScale));
                pass.SetVector(command, "_WeatherMapOffset", WeatherMapSpeed * Time.time / WeatherMapScale);
                pass.SetVector(command, "_WeatherMapSpeed", WeatherMapSpeed);

                pass.SetFloat(command, "_NoiseScale", MathUtils.Rcp(NoiseScale));
                pass.SetFloat(command, "_NoiseStrength", NoiseStrength);

                pass.SetFloat(command, "_DetailNoiseScale", MathUtils.Rcp(DetailScale));
                pass.SetFloat(command, "_DetailNoiseStrength", DetailStrength);

                pass.SetFloat(command, "_StartHeight", StartHeight);
                pass.SetFloat(command, "_LayerThickness", LayerThickness);
                pass.SetFloat(command, "_LightDistance", LightDistance);
                pass.SetFloat(command, "_Density", Density * MathUtils.Log2e);

                pass.SetFloat(command, "_TransmittanceThreshold", TransmittanceThreshold);

                pass.SetFloat(command, "_Samples", RaySamples);
                pass.SetFloat(command, "_LightSamples", LightSamples);

                pass.SetVector(command, "_NoiseResolution", (Vector3)NoiseResolution);
                pass.SetVector(command, "_DetailNoiseResolution", (Vector3)DetailNoiseResolution);
                pass.SetVector(command, "_WeatherMapResolution", (Vector2)WeatherMapResolution);

                pass.SetFloat(command, "_BackScatterPhase", BackScatterPhase);
                pass.SetFloat(command, "_ForwardScatterPhase", ForwardScatterPhase);
                pass.SetFloat(command, "_BackScatterScale", BackScatterScale);
                pass.SetFloat(command, "_ForwardScatterScale", ForwardScatterScale);
            }
        }

        private readonly Material material;
        private readonly Settings settings;
        private readonly PersistentRTHandleCache cloudLuminanceTextureCache, cloudTransmittanceTextureCache;
        private int version = -1;
        private readonly RTHandle weatherMap, noiseTexture, detailNoiseTexture;
        private readonly ComputeShader cloudCoverageComputeShader;

        public VolumetricClouds(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };

            cloudLuminanceTextureCache = new(GraphicsFormat.B10G11R11_UFloatPack32, renderGraph, "Cloud Luminance");
            cloudTransmittanceTextureCache = new(GraphicsFormat.R8_UNorm, renderGraph, "Cloud Transmittance");

            weatherMap = renderGraph.GetTexture(settings.WeatherMapResolution.x, settings.WeatherMapResolution.y, GraphicsFormat.R8_UNorm, isPersistent: true);
            noiseTexture = renderGraph.GetTexture(settings.NoiseResolution.x, settings.NoiseResolution.y, GraphicsFormat.R8_UNorm, settings.NoiseResolution.z, TextureDimension.Tex3D, isPersistent: true);
            detailNoiseTexture = renderGraph.GetTexture(settings.DetailNoiseResolution.x, settings.DetailNoiseResolution.y, GraphicsFormat.R8_UNorm, settings.DetailNoiseResolution.z, TextureDimension.Tex3D, isPersistent: true);

            cloudCoverageComputeShader = Resources.Load<ComputeShader>("CloudCoverage");
        }

        public void SetupData()
        {
            var result = new CloudData(weatherMap, noiseTexture, detailNoiseTexture);

            if (version >= settings.Version)
                return;

            version = settings.Version;

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Weather Map"))
            {
                pass.Initialize(material, 0);
                pass.WriteTexture(weatherMap, RenderBufferLoadAction.DontCare);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_WeatherMapFrequency", settings.WeatherMapNoiseParams.Frequency);
                    pass.SetFloat(command, "_WeatherMapH", settings.WeatherMapNoiseParams.H);
                    pass.SetFloat(command, "_WeatherMapOctaves", settings.WeatherMapNoiseParams.Octaves);
                    pass.SetFloat(command, "_WeatherMapFactor", settings.WeatherMapNoiseParams.FractalBound);
                    pass.SetVector(command, "_WeatherMapResolution", (Vector2)settings.WeatherMapResolution);
                });
            }

            // Noise
            var maxInstanceCount = 32;
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Noise Texture"))
            {
                var primitiveCount = MathUtils.DivRoundUp(settings.NoiseResolution.z, maxInstanceCount);
                pass.Initialize(material, 1, primitiveCount);
                pass.DepthSlice = -1;
                pass.WriteTexture(noiseTexture, RenderBufferLoadAction.DontCare);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_NoiseFrequency", settings.NoiseParams.Frequency);
                    pass.SetFloat(command, "_NoiseH", settings.NoiseParams.H);
                    pass.SetFloat(command, "_NoiseOctaves", settings.NoiseParams.Octaves);
                    pass.SetFloat(command, "_NoiseFactor", settings.NoiseParams.FractalBound);
                    pass.SetVector(command, "_NoiseResolution", (Vector3)settings.NoiseResolution);

                    pass.SetFloat(command, "_CellularNoiseFrequency", settings.CellularNoiseParams.Frequency);
                    pass.SetFloat(command, "_CellularNoiseH", settings.CellularNoiseParams.H);
                    pass.SetFloat(command, "_CellularNoiseOctaves", settings.CellularNoiseParams.Octaves);
                });
            }

            // Detail
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Detail Noise Texture"))
            {
                var primitiveCount = MathUtils.DivRoundUp(settings.DetailNoiseResolution.z, maxInstanceCount);
                pass.Initialize(material, 2, primitiveCount);
                pass.DepthSlice = -1;
                pass.WriteTexture(detailNoiseTexture, RenderBufferLoadAction.DontCare);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_DetailNoiseFrequency", settings.DetailNoiseParams.Frequency);
                    pass.SetFloat(command, "_DetailNoiseH", settings.DetailNoiseParams.H);
                    pass.SetFloat(command, "_DetailNoiseOctaves", settings.DetailNoiseParams.Octaves);
                    pass.SetFloat(command, "_DetailNoiseFactor", settings.DetailNoiseParams.FractalBound);
                    pass.SetVector(command, "_DetailNoiseResolution", (Vector3)settings.DetailNoiseResolution);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(result, renderGraph.FrameIndex, true);
        }

        public void RenderShadow(CullingResults cullingResults, Camera camera, float planetRadius)
        {
            var lightDirection = Vector3.up;
            var lightRotation = Quaternion.LookRotation(Vector3.down);

            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var light = cullingResults.visibleLights[i];
                if (light.lightType != LightType.Directional)
                    continue;

                lightDirection = -light.localToWorldMatrix.Forward();
                lightRotation = light.localToWorldMatrix.rotation;

                // Only 1 light supported
                break;
            }

            var radius = settings.ShadowRadius;
            var resolution = settings.ShadowResolution;
            var res = new Vector4(resolution, resolution, 1f / resolution, 1f / resolution);
            var cameraPosition = camera.transform.position;
            var texelSize = radius * 2.0f / resolution;
            var snappedCameraPosition = new Vector3(Mathf.Floor(cameraPosition.x / texelSize) * texelSize, Mathf.Floor(cameraPosition.y / texelSize) * texelSize, Mathf.Floor(cameraPosition.z / texelSize) * texelSize);

            var planetCenter = new Vector3(0.0f, -cameraPosition.y - planetRadius, 0.0f);
            var rayOrigin = new Vector3(snappedCameraPosition.x, 0.0f, snappedCameraPosition.z) - cameraPosition;

            // Transform camera bounds to light space
            var boundsMin = rayOrigin + new Vector3(-radius, 0.0f, -radius);
            var boundsSize = new Vector3(radius * 2f, settings.StartHeight + settings.LayerThickness, radius * 2f);
            var worldToLight = Quaternion.Inverse(lightRotation);
            var minValue = Vector3.positiveInfinity;
            var maxValue = Vector3.negativeInfinity;
            for (var z = 0; z < 2; z++)
            {
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 2; x++)
                    {
                        var worldPoint = boundsMin + Vector3.Scale(boundsSize, new Vector3(x, y, z));
                        var localPoint = worldToLight * worldPoint;
                        minValue = Vector3.Min(minValue, localPoint);
                        maxValue = Vector3.Max(maxValue, localPoint);

                        // Also raycast each point against the outer planet sphere in the light direction
                        if(GeometryUtilities.IntersectRaySphere(worldPoint - planetCenter, lightDirection, planetRadius + settings.StartHeight + settings.LayerThickness, out var hits) && hits.y > 0.0f)
                        {
                            var worldPoint1 = worldPoint + lightDirection * hits.y;
                            var localPoint1 = worldToLight * worldPoint1;
                            minValue = Vector3.Min(minValue, localPoint1);
                            maxValue = Vector3.Max(maxValue, localPoint1);
                        }
                    }
                }
            }

            var depth = maxValue.z - minValue.z;

            var viewMatrix = Matrix4x4.Rotate(worldToLight);
            var invViewMatrix = Matrix4x4.Rotate(lightRotation);

            var projectionMatrix = Matrix4x4Extensions.OrthoOffCenterNormalized(minValue.x, maxValue.x, minValue.y, maxValue.y, minValue.z, maxValue.z);
            var inverseProjectionMatrix = new Matrix4x4
            {
                m00 = 1.0f / settings.ShadowResolution * (maxValue.x - minValue.x),
                m03 = minValue.x,
                m11 = 1.0f / settings.ShadowResolution * (maxValue.y - minValue.y),
                m13 = minValue.y,
                m23 = minValue.z,
                m33 = 1.0f
            };

            var invViewProjection = invViewMatrix * inverseProjectionMatrix;
            var worldToShadow = projectionMatrix * viewMatrix;

            var cloudShadow = renderGraph.GetTexture(settings.ShadowResolution, settings.ShadowResolution, GraphicsFormat.B10G11R11_UFloatPack32);

            var cloudShadowData = new CloudShadowData(invViewProjection, -lightDirection, 1f / depth, 1f / settings.Density, settings.ShadowSamples, 0.0f, 0.0f);
            var cloudShadowDataBuffer = renderGraph.SetConstantBuffer(cloudShadowData);

            using(var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Cloud Shadow"))
            {
                pass.Initialize(material, 3);
                pass.WriteTexture(cloudShadow, RenderBufferLoadAction.DontCare);
                pass.ReadBuffer("CloudShadowData", cloudShadowDataBuffer);
                pass.AddRenderPassData<CloudData>();

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    settings.SetCloudPassData(command, pass);
                    pass.SetVector(command, "_ViewPosition", camera.transform.position);
                });
            }

            // Cloud coverage
            var cloudCoverageBufferTemp = renderGraph.GetBuffer(1, 16, GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
            var cloudCoverageBuffer = renderGraph.GetBuffer(1, 16, GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination);

            var result = new CloudShadowDataResult(cloudShadow, depth, worldToShadow, settings.Density, cloudCoverageBuffer, planetRadius, planetRadius + settings.StartHeight + settings.LayerThickness);
            renderGraph.ResourceMap.SetRenderPassData(result, renderGraph.FrameIndex);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Cloud Coverage"))
            {
                Color lightColor0 = Color.clear, lightColor1 = Color.clear;
                Vector3 lightDirection0 = Vector3.up, lightDirection1 = Vector3.up;

                // Find first 2 directional lights
                var dirLightCount = 0;
                for (var i = 0; i < cullingResults.visibleLights.Length; i++)
                {
                    var light = cullingResults.visibleLights[i];
                    if (light.lightType != LightType.Directional)
                        continue;

                    dirLightCount++;

                    if (dirLightCount == 1)
                    {
                        lightDirection0 = -light.localToWorldMatrix.Forward();
                        lightColor0 = light.finalColor;
                    }
                    else if (dirLightCount == 2)
                    {
                        lightDirection1 = -light.localToWorldMatrix.Forward();
                        lightColor1 = light.finalColor;

                        // Only 2 lights supported
                        break;
                    }
                }

                pass.Initialize(cloudCoverageComputeShader, 0, 1);

                pass.AddRenderPassData<CloudData>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.WriteBuffer("_Result", cloudCoverageBufferTemp);
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<CloudShadowDataResult>();

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    settings.SetCloudPassData(command, pass);

                    pass.SetVector(command, "_LightDirection0", lightDirection0);
                    pass.SetVector(command, "_LightColor0", lightColor0);
                    pass.SetVector(command, "_LightDirection1", lightDirection1);
                    pass.SetVector(command, "_LightColor1", lightColor1);

                    pass.SetVector(command, "_ViewPosition", camera.transform.position);
                    pass.SetFloat(command, "_ViewHeight", camera.transform.position.y + planetRadius);
                });
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Cloud Coverage Copy"))
            {
                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    command.CopyBuffer(cloudCoverageBufferTemp, cloudCoverageBuffer);
                });
            }
        }

        public readonly struct CloudShadowDataResult : IRenderPassData
        {
            private readonly RTHandle cloudShadow;
            private readonly float cloudDepthInvScale, cloudShadowExtinctionInvScale;
            private readonly Matrix4x4 worldToCloudShadow;
            private readonly BufferHandle cloudCoverageBuffer;
            private readonly float startHeight, endHeight;

            public CloudShadowDataResult(RTHandle cloudShadow, float cloudDepthInvScale, Matrix4x4 worldToCloudShadow, float cloudShadowExtinctionInvScale, BufferHandle cloudCoverageBuffer, float startHeight, float endHeight)
            {
                this.cloudShadow = cloudShadow;
                this.cloudDepthInvScale = cloudDepthInvScale;
                this.worldToCloudShadow = worldToCloudShadow;
                this.cloudShadowExtinctionInvScale = cloudShadowExtinctionInvScale;
                this.cloudCoverageBuffer = cloudCoverageBuffer;
                this.startHeight = startHeight;
                this.endHeight = endHeight;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_CloudShadow", cloudShadow);
                pass.ReadBuffer("CloudCoverage", cloudCoverageBuffer);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetMatrix(command, "_WorldToCloudShadow", worldToCloudShadow);
                pass.SetFloat(command, "_CloudShadowDepthInvScale", cloudDepthInvScale);
                pass.SetFloat(command, "_CloudShadowExtinctionInvScale", cloudShadowExtinctionInvScale);
                pass.SetFloat(command, "_CloudCoverageStart", cloudShadowExtinctionInvScale);
                pass.SetFloat(command, "_CloudShadowExtinctionInvScale", cloudShadowExtinctionInvScale);

                // This is used to scale a smooth falloff that uses distance^2
                var cloudCoverageScale = 1.0f / (startHeight * startHeight - endHeight * endHeight);
                var cloudCoverageOffset = -endHeight * endHeight / (startHeight * startHeight - endHeight * endHeight);
                pass.SetFloat(command, "_CloudCoverageScale", cloudCoverageScale);
                pass.SetFloat(command, "_CloudCoverageOffset", cloudCoverageOffset);

                pass.SetVector(command, "_CloudShadowScaleLimit", cloudShadow.ScaleLimit2D);
            }
        }

        public void Render(RTHandle cameraDepth, int width, int height, Vector2 jitter, float fov, float aspect, Matrix4x4 viewToWorld, IRenderPassData commonPassData, Camera camera, CullingResults cullingResults, float viewHeight)
        {
            Color lightColor0 = Color.clear, lightColor1 = Color.clear;
            Vector3 lightDirection0 = Vector3.up, lightDirection1 = Vector3.up;

            // Find first 2 directional lights
            var dirLightCount = 0;
            for (var i = 0; i < cullingResults.visibleLights.Length; i++)
            {
                var light = cullingResults.visibleLights[i];
                if (light.lightType != LightType.Directional)
                    continue;

                dirLightCount++;

                if (dirLightCount == 1)
                {
                    lightDirection0 = -light.localToWorldMatrix.Forward();
                    lightColor0 = light.finalColor;
                }
                else if (dirLightCount == 2)
                {
                    lightDirection1 = -light.localToWorldMatrix.Forward();
                    lightColor1 = light.finalColor;

                    // Only 2 lights supported
                    break;
                }
            }

            var cloudLuminanceTemp = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
            var cloudTransmittanceTemp = renderGraph.GetTexture(width, height, GraphicsFormat.R8_UNorm, isScreenTexture: true);
            var cloudDepth = renderGraph.GetTexture(width, height, GraphicsFormat.R32G32_SFloat, isScreenTexture: true);

            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Render"))
            {
                // Determine pass
                string keyword = string.Empty;
                var viewHeight1 = camera.transform.position.y;
                if(viewHeight > settings.StartHeight)
                {
                    if(viewHeight1 > settings.StartHeight + settings.LayerThickness)
                    {
                        keyword = "ABOVE_CLOUD_LAYER";
                    }
                }
                else
                {
                    keyword = "BELOW_CLOUD_LAYER";
                }

                pass.Initialize(material, 4, 1, keyword, camera);
                pass.WriteTexture(cloudLuminanceTemp, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(cloudTransmittanceTemp, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(cloudDepth, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Depth", cameraDepth);
                pass.AddRenderPassData<CloudData>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<CloudShadowDataResult>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();

                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    settings.SetCloudPassData(command, pass);
                    pass.SetVector(command, "_LightDirection0", lightDirection0);
                    pass.SetVector(command, "_LightColor0", lightColor0);
                    pass.SetVector(command, "_LightDirection1", lightDirection1);
                    pass.SetVector(command, "_LightColor1", lightColor1);

                    pass.SetFloat(command, "_ViewHeight", viewHeight);

                    commonPassData.SetProperties(pass, command);
                });
            }

            // Reprojection
            var (luminanceCurrent, luminanceHistory, luminanceWasCreated) = cloudLuminanceTextureCache.GetTextures(width, height, camera, true);
            var (transmittanceCurrent, transmittanceHistory, transmittanceWasCreated) = cloudTransmittanceTextureCache.GetTextures(width, height, camera, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Temporal"))
            {
                pass.Initialize(material, 5, camera: camera);
                pass.WriteTexture(luminanceCurrent, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(transmittanceCurrent, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_Input", cloudLuminanceTemp);
                pass.ReadTexture("_InputTransmittance", cloudTransmittanceTemp);
                pass.ReadTexture("_History", luminanceHistory);
                pass.ReadTexture("_TransmittanceHistory", transmittanceHistory);
                pass.ReadTexture("CloudDepthTexture", cloudDepth);
                pass.ReadTexture("_Depth", cameraDepth);
                commonPassData.SetInputs(pass);
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_IsFirst", luminanceWasCreated ? 1.0f : 0.0f);
                    pass.SetFloat(command, "_StationaryBlend", settings.StationaryBlend);
                    pass.SetFloat(command, "_MotionBlend", settings.MotionBlend);
                    pass.SetFloat(command, "_MotionFactor", settings.MotionFactor);

                    pass.SetVector(command, "_HistoryScaleLimit", luminanceHistory.ScaleLimit2D);
                    pass.SetVector(command, "_TransmittanceHistoryScaleLimit", transmittanceHistory.ScaleLimit2D);

                    pass.SetInt(command, "_MaxWidth", width - 1);
                    pass.SetInt(command, "_MaxHeight", height - 1);
                    pass.SetFloat(command, "_ViewHeight", viewHeight);

                    settings.SetCloudPassData(command, pass);
                    commonPassData.SetProperties(pass, command);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new CloudRenderResult(luminanceCurrent, transmittanceCurrent, cloudDepth), renderGraph.FrameIndex);
        }

        private class PassData
        {
        }

        public readonly struct CloudData : IRenderPassData
        {
            private readonly RTHandle weatherMap, noiseTexture, detailNoiseTexture;

            public CloudData(RTHandle weatherMap, RTHandle noiseTexture, RTHandle detailNoiseTexture)
            {
                this.weatherMap = weatherMap;
                this.noiseTexture = noiseTexture;
                this.detailNoiseTexture = detailNoiseTexture;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_WeatherMap", weatherMap);
                pass.ReadTexture("_CloudNoise", noiseTexture);
                pass.ReadTexture("_CloudDetailNoise", detailNoiseTexture);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
            }
        }

        public readonly struct CloudRenderResult : IRenderPassData
        {
            private readonly RTHandle cloudTexture, cloudTransmittanceTexture, cloudDepth;

            public CloudRenderResult(RTHandle cloudLuminanceTexture, RTHandle cloudTransmittanceTexture, RTHandle cloudDepth)
            {
                this.cloudTexture = cloudLuminanceTexture ?? throw new ArgumentNullException(nameof(cloudLuminanceTexture));
                this.cloudTransmittanceTexture = cloudTransmittanceTexture;
                this.cloudDepth = cloudDepth ?? throw new ArgumentNullException(nameof(cloudDepth));
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("CloudTexture", cloudTexture);
                pass.ReadTexture("CloudTransmittanceTexture", cloudTransmittanceTexture);
                pass.ReadTexture("CloudDepthTexture", cloudDepth);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector(command, "CloudTextureScaleLimit", cloudTexture.ScaleLimit2D);
                pass.SetVector(command, "CloudTransmittanceTextureScaleLimit", cloudTransmittanceTexture.ScaleLimit2D);
            }
        }
    }
}

struct CloudShadowData
{
    private Matrix4x4 invViewProjection;
    private Vector3 vector3;
    private float v1;
    private float v2;
    private float shadowSamples;
    private float v3;
    private float v4;

    public CloudShadowData(Matrix4x4 invViewProjection, Vector3 vector3, float v1, float v2, float shadowSamples, float v3, float v4)
    {
        this.invViewProjection = invViewProjection;
        this.vector3 = vector3;
        this.v1 = v1;
        this.v2 = v2;
        this.shadowSamples = shadowSamples;
        this.v3 = v3;
        this.v4 = v4;
    }
}