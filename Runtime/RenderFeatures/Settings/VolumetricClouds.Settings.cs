using System;
using UnityEngine;

public partial class VolumetricClouds
{
    [Serializable]
    public class Settings
    {
        [field: Header("Weather Map")]
        [field: SerializeField] public Int2 WeatherMapResolution { get; private set; } = new(256, 256);
        [field: SerializeField] public float WeatherMapScale { get; private set; } = 32768.0f;
        [field: SerializeField] public Float2 WeatherMapSpeed { get; private set; } = Vector2.zero;
        [field: SerializeField, Range(0.0f, 1.0f)] public float WeatherMapStrength { get; private set; } = 1.0f;
        [field: SerializeField] public FractalNoiseParameters WeatherMapNoiseParams { get; private set; }

        [field: Header("Noise Texture")]
        [field: SerializeField] public Int3 NoiseResolution { get; private set; } = new(128, 64, 128);
        [field: SerializeField] public float NoiseScale { get; private set; } = 4096.0f;
        [field: SerializeField, Range(0.0f, 1.0f)] public float NoiseStrength { get; private set; } = 1.0f;
        [field: SerializeField] public FractalNoiseParameters NoiseParams { get; private set; }
        [field: SerializeField] public FractalNoiseParameters CellularNoiseParams { get; private set; }

        [field: Header("Detail Noise Texture")]
        [field: SerializeField] public Int3 DetailNoiseResolution { get; private set; } = new(32, 32, 32);
        [field: SerializeField] public float DetailScale { get; private set; } = 512.0f;
        [field: SerializeField, Range(0.0f, 1.0f)] public float DetailStrength { get; private set; } = 1.0f;
        [field: SerializeField] public FractalNoiseParameters DetailNoiseParams { get; private set; }

		[field: Header("High Altitude Clouds")]
		[field: SerializeField] public Int2 HighAltitudeMapResolution { get; private set; } = new(256, 256);
		[field: SerializeField] public float HighAltitudeMapHeight { get; private set; } = 4096;
		[field: SerializeField] public float HighAltitudeMapDensity { get; private set; } = 0.5f;
		[field: SerializeField] public float HighAltitudeMapScale { get; private set; } = 32768.0f;
		[field: SerializeField] public Float2 HighAltitudeMapSpeed { get; private set; } = Vector2.zero;
		[field: SerializeField, Range(0.0f, 1.0f)] public float HighAltitudeMapStrength { get; private set; } = 1.0f;
		[field: SerializeField] public FractalNoiseParameters HighAltitudeMapNoiseParams { get; private set; }

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

		[field: SerializeField, Range(1, 8)] public int ScatterOctaves { get; private set; } = 2;
		[field: SerializeField, Range(0, 1)] public float ScatterAttenuation { get; private set; } = 0.5f;
		[field: SerializeField, Range(0, 1)] public float ScatterContribution { get; private set; } = 0.5f;
		[field: SerializeField, Range(0, 1)] public float ScatterEccentricityAttenuation { get; private set; } = 0.5f;

		[field: SerializeField, Range(-1.0f, 1.0f)] public float BackScatterPhase { get; private set; } = -0.5f;
        [field: SerializeField, Range(-1.0f, 1.0f)] public float ForwardScatterPhase { get; private set; } = 0.8f;

        [field: Header("Temporal")]
        [field: SerializeField, Range(0.0f, 1.0f)] public float StationaryBlend { get; private set; } = 0.95f;
        [field: SerializeField, Range(0.0f, 1.0f)] public float MotionBlend { get; private set; } = 0.0f;
        [field: SerializeField, Min(0.0f)] public float MotionFactor { get; private set; } = 6000.0f;
		[field: SerializeField, Min(0.0f)] public float DepthThreshold { get; private set; } = 1.0f;

        [field: NonSerialized] public int Version { get; private set; }

        public void SetCloudPassData(RenderPass pass, float deltaTime)
        {
			// TODO: Cbuffer
            pass.SetFloat("_WeatherMapStrength", WeatherMapStrength);
            pass.SetFloat("_WeatherMapScale", Math.Rcp(WeatherMapScale));
            pass.SetVector("_WeatherMapOffset", WeatherMapSpeed * deltaTime / WeatherMapScale);

            pass.SetFloat("_NoiseScale", Math.Rcp(NoiseScale));
            pass.SetFloat("_NoiseStrength", NoiseStrength);

            pass.SetFloat("_DetailNoiseScale", Math.Rcp(DetailScale));
            pass.SetFloat("_DetailNoiseStrength", DetailStrength);

            pass.SetFloat("_StartHeight", StartHeight);
            pass.SetFloat("_LayerThickness", LayerThickness);
            pass.SetFloat("_LightDistance", LightDistance);
            pass.SetFloat("_Density", Density * Math.Log2e);

            pass.SetFloat("_TransmittanceThreshold", TransmittanceThreshold);

            pass.SetFloat("_Samples", RaySamples);
            pass.SetFloat("_LightSamples", LightSamples);

            pass.SetVector("_NoiseResolution", NoiseResolution);
            pass.SetVector("_DetailNoiseResolution", DetailNoiseResolution);
            pass.SetVector("_WeatherMapResolution", WeatherMapResolution);

			pass.SetVector("HighAltitudeMapResolution", HighAltitudeMapResolution);
			pass.SetFloat("HighAltitudeMapScale", HighAltitudeMapScale);
			pass.SetVector("HighAltitudeMapSpeed", HighAltitudeMapSpeed);
			pass.SetFloat("HighAltitudeMapStrength", HighAltitudeMapStrength);
			pass.SetFloat("HighAltitudeMapHeight", HighAltitudeMapHeight);
			pass.SetFloat("HighAltitudeMapDensity", HighAltitudeMapDensity);

			pass.SetFloat("_BackScatterPhase", BackScatterPhase);
            pass.SetFloat("_ForwardScatterPhase", ForwardScatterPhase);

			pass.SetFloat("ScatterOctaves", ScatterOctaves);
			pass.SetFloat("ScatterAttenuation", ScatterAttenuation);
			pass.SetFloat("ScatterContribution", ScatterContribution);
			pass.SetFloat("ScatterEccentricityAttenuation", ScatterEccentricityAttenuation);
		}
	}
}