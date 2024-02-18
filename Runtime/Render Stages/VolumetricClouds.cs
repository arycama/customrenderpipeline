using System;
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

            [field: Header("Cloud Settings")]
            [field: SerializeField, Range(0.0f, 0.1f)] public float Density { get; private set; } = 0.05f;
            [field: SerializeField] public float StartHeight { get; private set; } = 1024.0f;
            [field: SerializeField] public float LayerThickness { get; private set; } = 512.0f;
            [field: SerializeField] public int RaySamples { get; private set; } = 32;
            [field: SerializeField] public int LightSamples { get; private set; } = 5;
            [field: SerializeField] public float LightDistance { get; private set; } = 512.0f;

            [field: Header("Temporal Settings")]
            [field: SerializeField, Range(0.0f, 1.0f)] public float StationaryBlend { get; private set; } = 0.95f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float MotionBlend { get; private set; } = 0.0f;
            [field: SerializeField, Min(0.0f)] public float MotionFactor { get; private set; } = 6000.0f;
        }

        private readonly Material material;
        private readonly Settings settings;
        private readonly CameraTextureCache textureCache;

        public VolumetricClouds(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };
            textureCache = new(renderGraph, "Volumetric Clouds");
        }

        public RTHandle Render(RTHandle cameraDepth, int width, int height, Vector2 jitter, float fov, float aspect, Matrix4x4 viewToWorld, IRenderPassData commonPassData, Camera camera, out RTHandle cloudDepth, CullingResults cullingResults)
        {
            var weatherMap = renderGraph.GetTexture(settings.WeatherMapResolution.x, settings.WeatherMapResolution.y, GraphicsFormat.R8_UNorm, hasMips: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Weather Map"))
            {
                pass.Initialize(material, 0);
                pass.WriteTexture(weatherMap);

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
                pass.WriteTexture(noiseTexture);

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
                pass.WriteTexture(detailNoiseTexture);

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


            var cloudTemp = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat);
            cloudDepth = renderGraph.GetTexture(width, height, GraphicsFormat.R16_SFloat);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Render"))
            {
                pass.Initialize(material, 3);
                pass.WriteTexture(cloudTemp);
                pass.WriteTexture(cloudDepth);
                pass.ConfigureClear(RTClearFlags.Color, Color.black);

                pass.WriteDepth(cameraDepth, RenderTargetFlags.ReadOnlyDepthStencil);

                pass.ReadTexture("_Depth", cameraDepth);
                pass.ReadTexture("_WeatherMap", weatherMap);
                pass.ReadTexture("_CloudNoise", noiseTexture);
                pass.ReadTexture("_CloudDetailNoise", detailNoiseTexture);

                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    command.GenerateMips(detailNoiseTexture);
                    pass.SetFloat(command, "_WeatherMapStrength", settings.WeatherMapStrength);
                    pass.SetFloat(command, "_WeatherMapScale", MathUtils.Rcp(settings.WeatherMapScale));
                    pass.SetVector(command, "_WeatherMapOffset", settings.WeatherMapSpeed * Time.time / settings.WeatherMapScale);
                    pass.SetVector(command, "_WeatherMapSpeed", settings.WeatherMapSpeed);

                    pass.SetFloat(command, "_NoiseScale", MathUtils.Rcp(settings.NoiseScale));
                    pass.SetFloat(command, "_NoiseStrength", settings.NoiseStrength);

                    pass.SetFloat(command, "_DetailNoiseScale", MathUtils.Rcp(settings.DetailScale));
                    pass.SetFloat(command, "_DetailNoiseStrength", settings.DetailStrength);

                    pass.SetFloat(command, "_StartHeight", settings.StartHeight);
                    pass.SetFloat(command, "_LayerThickness", settings.LayerThickness);
                    pass.SetFloat(command, "_LightDistance", settings.LightDistance);
                    pass.SetFloat(command, "_Density", settings.Density);

                    pass.SetInt(command, "_RaySamples", settings.RaySamples);
                    pass.SetInt(command, "_LightSamples", settings.LightSamples);

                    pass.SetVector(command, "_NoiseResolution", (Vector3)settings.NoiseResolution);
                    pass.SetVector(command, "_DetailNoiseResolution", (Vector3)settings.DetailNoiseResolution);
                    pass.SetVector(command, "_WeatherMapResolution", (Vector2)settings.WeatherMapResolution);

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

                    commonPassData.SetProperties(pass, command);
                });
            }

            // Reprojection
            var isFirst = textureCache.GetTexture(camera, new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_SFloat, 0), out var current, out var previous);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Volumetric Clouds Temporal"))
            {
                pass.Initialize(material, 4);
                pass.WriteTexture(current);
                pass.ReadTexture("_Input", cloudTemp);
                pass.ReadTexture("_History", previous);
                pass.ReadTexture("_CloudDepth", cloudDepth);

                var data = pass.SetRenderFunction<PassData>((command, context, pass, data) =>
                {
                    pass.SetFloat(command, "_IsFirst", isFirst ? 1.0f : 0.0f);
                    pass.SetFloat(command, "_StationaryBlend", settings.StationaryBlend);
                    pass.SetFloat(command, "_MotionBlend", settings.MotionBlend);
                    pass.SetFloat(command, "_MotionFactor", settings.MotionFactor);
                });
            }

            return current;
        }

        private class PassData
        {
        }
    }
}