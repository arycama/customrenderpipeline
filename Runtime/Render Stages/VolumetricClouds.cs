using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
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

            [field: Header("Temporal")]
            [field: SerializeField, Range(0.0f, 1.0f)] public float StationaryBlend { get; private set; } = 0.95f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float MotionBlend { get; private set; } = 0.0f;
            [field: SerializeField, Min(0.0f)] public float MotionFactor { get; private set; } = 6000.0f;

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
                pass.SetFloat(command, "_Density", Density);

                pass.SetFloat(command, "_TransmittanceThreshold", TransmittanceThreshold);

                pass.SetInt(command, "_RaySamples", RaySamples);
                pass.SetInt(command, "_LightSamples", LightSamples);

                pass.SetVector(command, "_NoiseResolution", (Vector3)NoiseResolution);
                pass.SetVector(command, "_DetailNoiseResolution", (Vector3)DetailNoiseResolution);
                pass.SetVector(command, "_WeatherMapResolution", (Vector2)WeatherMapResolution);
            }
        }

        private readonly Material material;
        private readonly Settings settings;
        private readonly CameraTextureCache textureCache;
        private CustomSampler sampler;

        public VolumetricClouds(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(renderGraph, "Volumetric Clouds");
            sampler = CustomSampler.Create("Volumetric Clouds", true);
        }

        public IRenderPassData SetupData()
        {
            var weatherMap = renderGraph.GetTexture(settings.WeatherMapResolution.x, settings.WeatherMapResolution.y, GraphicsFormat.R8_UNorm, hasMips: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Weather Map"))
            {
                pass.Initialize(material, 0);
                pass.WriteTexture(weatherMap, RenderBufferLoadAction.DontCare);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
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
            var noiseTexture = renderGraph.GetTexture(settings.NoiseResolution.x, settings.NoiseResolution.y, GraphicsFormat.R8_UNorm, false, settings.NoiseResolution.z, TextureDimension.Tex3D, false, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Noise Texture"))
            {
                var primitiveCount = MathUtils.DivRoundUp(settings.NoiseResolution.z, maxInstanceCount);
                pass.Initialize(material, 1, primitiveCount);
                pass.DepthSlice = -1;
                pass.WriteTexture(noiseTexture, RenderBufferLoadAction.DontCare);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.GenerateMips(weatherMap);
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
            var detailNoiseTexture = renderGraph.GetTexture(settings.DetailNoiseResolution.x, settings.DetailNoiseResolution.y, GraphicsFormat.R8_UNorm, false, settings.DetailNoiseResolution.z, TextureDimension.Tex3D, false, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Detail Noise Texture"))
            {
                var primitiveCount = MathUtils.DivRoundUp(settings.DetailNoiseResolution.z, maxInstanceCount);
                pass.Initialize(material, 2, primitiveCount);
                pass.DepthSlice = -1;
                pass.WriteTexture(detailNoiseTexture, RenderBufferLoadAction.DontCare);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.GenerateMips(noiseTexture);
                    pass.SetFloat(command, "_DetailNoiseFrequency", settings.DetailNoiseParams.Frequency);
                    pass.SetFloat(command, "_DetailNoiseH", settings.DetailNoiseParams.H);
                    pass.SetFloat(command, "_DetailNoiseOctaves", settings.DetailNoiseParams.Octaves);
                    pass.SetFloat(command, "_DetailNoiseFactor", settings.DetailNoiseParams.FractalBound);
                    pass.SetVector(command, "_DetailNoiseResolution", (Vector3)settings.DetailNoiseResolution);
                });
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Volumetric Clouds Detail Noise Texture"))
            {
                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.GenerateMips(detailNoiseTexture);
                });
            }

            return new CloudData(weatherMap, noiseTexture, detailNoiseTexture);
        }

        public IRenderPassData RenderShadow(CullingResults cullingResults, Camera camera, IRenderPassData cloudRenderData, float planetRadius)
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

            var viewPosition = lightDirection * (settings.StartHeight + settings.LayerThickness - camera.transform.position.y);
            var viewMatrix = Matrix4x4.TRS(viewPosition, lightRotation, new Vector3(1f, 1f, -1f)).inverse;
            var projectionMatrix = Matrix4x4.Ortho(-radius, radius, -radius, radius, -radius, radius);
            var projectionMatrix2 = Matrix4x4.Ortho(-radius, radius, -radius, radius, 0, radius);

            var viewProjection = projectionMatrix * viewMatrix;
            var worldToShadow = (projectionMatrix2 * viewMatrix).ConvertToAtlasMatrix();

            var cloudShadow = renderGraph.GetTexture(settings.ShadowResolution, settings.ShadowResolution, GraphicsFormat.B10G11R11_UFloatPack32);
            using(var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Cloud Shadow"))
            {
                pass.Initialize(material, 3);
                pass.WriteTexture(cloudShadow, RenderBufferLoadAction.DontCare);
                cloudRenderData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    settings.SetCloudPassData(command, pass);

                    pass.SetFloat(command, "_CloudDepthScale", 1f / planetRadius);
                    pass.SetVector(command, "_ScreenSizeCloudShadow", res);
                    pass.SetMatrix(command, "_InvViewProjMatrixCloudShadow", viewProjection.inverse);
                    pass.SetMatrix(command, "_WorldToCloudShadow", worldToShadow);
                    pass.SetFloat(command, "_CloudDepthInvScale", radius);
                    pass.SetVector(command, "_LightDirection0", -lightDirection);
                    pass.SetFloat(command, "_ShadowSamples", settings.ShadowSamples);
                });
            }

            return new CloudShadowData(cloudShadow, radius, worldToShadow);
        }

        struct CloudShadowData : IRenderPassData
        {
            private RTHandle cloudShadow;
            private float cloudDepthInvScale;
            private Matrix4x4 worldToCloudShadow;

            public CloudShadowData(RTHandle cloudShadow, float cloudDepthInvScale, Matrix4x4 worldToCloudShadow)
            {
                this.cloudShadow = cloudShadow;
                this.cloudDepthInvScale = cloudDepthInvScale;
                this.worldToCloudShadow = worldToCloudShadow;
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("_CloudShadow", cloudShadow);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetFloat(command, "_CloudDepthInvScale", cloudDepthInvScale);
                pass.SetMatrix(command, "_WorldToCloudShadow", worldToCloudShadow);
            }
        }

        public RTHandle Render(RTHandle cameraDepth, int width, int height, Vector2 jitter, float fov, float aspect, Matrix4x4 viewToWorld, IRenderPassData commonPassData, Camera camera, out RTHandle cloudDepth, CullingResults cullingResults, IRenderPassData cloudRenderData, IRenderPassData cloudShadow)
        {
            var cloudTemp = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
            cloudDepth = renderGraph.GetTexture(width, height, GraphicsFormat.R32_SFloat, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Render"))
            {
                // Determine pass
                string keyword = string.Empty;
                var viewHeight = camera.transform.position.y;
                if(viewHeight > settings.StartHeight)
                {
                    if(viewHeight > settings.StartHeight + settings.LayerThickness)
                    {
                        keyword = "ABOVE_CLOUD_LAYER";
                    }
                }
                else
                {
                    keyword = "BELOW_CLOUD_LAYER";
                }

                pass.Initialize(material, 4, 1, keyword);
                pass.WriteTexture(cloudTemp, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(cloudDepth, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Depth", cameraDepth);

                cloudRenderData.SetInputs(pass);
                cloudShadow.SetInputs(pass);
                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.BeginSample(sampler);
                    settings.SetCloudPassData(command, pass);

                    pass.SetMatrix(command, "_PixelToWorldViewDir", Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(width, height, jitter, fov, aspect, viewToWorld));

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

                    pass.SetVector(command, "_LightDirection0", lightDirection0);
                    pass.SetVector(command, "_LightColor0", lightColor0);
                    pass.SetVector(command, "_LightDirection1", lightDirection1);
                    pass.SetVector(command, "_LightColor1", lightColor1);

                    cloudShadow.SetProperties(pass, command);
                    cloudRenderData.SetProperties(pass, command);
                    commonPassData.SetProperties(pass, command);
                });
            }

            // Reprojection
            var isFirst = textureCache.GetTexture(camera, new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_SFloat, 0), out var current, out var previous);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Temporal"))
            {
                pass.Initialize(material, 5);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("_Input", cloudTemp);
                pass.ReadTexture("_History", previous);
                pass.ReadTexture("_CloudDepth", cloudDepth);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.EndSample(sampler);
                    //Debug.Log(sampler.GetRecorder().gpuElapsedNanoseconds / 1000000.0f);

                    pass.SetFloat(command, "_IsFirst", isFirst ? 1.0f : 0.0f);
                    pass.SetFloat(command, "_StationaryBlend", settings.StationaryBlend);
                    pass.SetFloat(command, "_MotionBlend", settings.MotionBlend);
                    pass.SetFloat(command, "_MotionFactor", settings.MotionFactor);

                    pass.SetMatrix(command, "_PixelToWorldViewDir", Matrix4x4Extensions.PixelToWorldViewDirectionMatrix(width, height, jitter, fov, aspect, viewToWorld));
                });
            }

            return current;
        }

        private class PassData
        {
        }

        public struct CloudData : IRenderPassData
        {
            private RTHandle weatherMap, noiseTexture, detailNoiseTexture;

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
    }
}