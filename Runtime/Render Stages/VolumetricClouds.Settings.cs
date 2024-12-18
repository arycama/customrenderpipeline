using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public partial class VolumetricClouds
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

            public void SetCloudPassData(RenderPass pass)
            {
                // TODO: Make this a render pass data?
                pass.SetFloat("_WeatherMapStrength", WeatherMapStrength);
                pass.SetFloat("_WeatherMapScale", MathUtils.Rcp(WeatherMapScale));
                pass.SetVector("_WeatherMapOffset", WeatherMapSpeed * (1.0f) / WeatherMapScale);
                pass.SetVector("_WeatherMapSpeed", WeatherMapSpeed);

                pass.SetFloat("_NoiseScale", MathUtils.Rcp(NoiseScale));
                pass.SetFloat("_NoiseStrength", NoiseStrength);

                pass.SetFloat("_DetailNoiseScale", MathUtils.Rcp(DetailScale));
                pass.SetFloat("_DetailNoiseStrength", DetailStrength);

                pass.SetFloat("_StartHeight", StartHeight);
                pass.SetFloat("_LayerThickness", LayerThickness);
                pass.SetFloat("_LightDistance", LightDistance);
                pass.SetFloat("_Density", Density * MathUtils.Log2e);

                pass.SetFloat("_TransmittanceThreshold", TransmittanceThreshold);

                pass.SetFloat("_Samples", RaySamples);
                pass.SetFloat("_LightSamples", LightSamples);

                pass.SetVector("_NoiseResolution", (Vector3)NoiseResolution);
                pass.SetVector("_DetailNoiseResolution", (Vector3)DetailNoiseResolution);
                pass.SetVector("_WeatherMapResolution", (Vector2)WeatherMapResolution);

                pass.SetFloat("_BackScatterPhase", BackScatterPhase);
                pass.SetFloat("_ForwardScatterPhase", ForwardScatterPhase);
                pass.SetFloat("_BackScatterScale", BackScatterScale);
                pass.SetFloat("_ForwardScatterScale", ForwardScatterScale);
            }
        }
    }
}
