using System;
using UnityEngine;

public partial class Sky
{
	[Serializable]
	public class Settings
	{
		[field: Header("Atmosphere Properties")]
		[field: SerializeField, Range(0.0f, 1.0f)] public float EarthScale { get; private set; } = 1.0f;
		[field: SerializeField] public Vector3 RayleighScatter { get; private set; } = new Vector3(5.802e-6f, 13.558e-6f, 33.1e-6f);
		[field: SerializeField] public float RayleighHeight { get; private set; } = 8000.0f;
		[field: SerializeField, Range(0, 1e-1f)] public float MieScatter { get; private set; } = 3.996e-6f;
		[field: SerializeField] public float MieAbsorption { get; private set; } = 4.4e-6f;
		[field: SerializeField] public float MieHeight { get; private set; } = 1200.0f;
		[field: SerializeField, Range(-1.0f, 1.0f)] public float MiePhase { get; private set; } = 0.8f;
		[field: SerializeField] public Vector3 OzoneAbsorption { get; private set; } = new Vector3(0.65e-6f, 1.881e-6f, 0.085e-6f);
		[field: SerializeField] public float OzoneWidth { get; private set; } = 15000.0f;
		[field: SerializeField] public float OzoneHeight { get; private set; } = 25000.0f;
		[field: SerializeField] public float CloudScatter { get; private set; } = 3.996e-6f;
		[field: SerializeField] public float PlanetRadius { get; private set; } = 6360000.0f;
		[field: SerializeField] public float AtmosphereHeight { get; private set; } = 100000.0f;
		[field: SerializeField] public Color GroundColor { get; private set; } = Color.grey;
		[field: SerializeField] public Cubemap StarMap { get; private set; }

		[field: Header("Star Properties")]
		[field: SerializeField] public int StarResolution = 512;
		[field: SerializeField] public int StarCount = 10000;
        [field: SerializeField] public Material StarMaterial;

		[field: Header("Transmittance Lookup")]
		[field: SerializeField] public int TransmittanceWidth { get; private set; } = 128;
		[field: SerializeField] public int TransmittanceHeight { get; private set; } = 64;
		[field: SerializeField] public int TransmittanceSamples { get; private set; } = 64;

		[field: Header("Luminance Lookup")]
		[field: SerializeField] public int LuminanceWidth { get; private set; } = 128;
		[field: SerializeField] public int LuminanceHeight { get; private set; } = 64;
		[field: SerializeField] public int LuminanceSamples { get; private set; } = 64;

		[field: Header("CDF Lookup")]
		[field: SerializeField] public int CdfWidth { get; private set; } = 64;
		[field: SerializeField] public int CdfHeight { get; private set; } = 64;
		[field: SerializeField] public int CdfSamples { get; private set; } = 64;

		[field: Header("Multi Scatter Lookup")]
		[field: SerializeField] public int MultiScatterWidth { get; private set; } = 32;
		[field: SerializeField] public int MultiScatterHeight { get; private set; } = 32;
		[field: SerializeField] public int MultiScatterSamples { get; private set; } = 64;

		[field: Header("Ambient Ground Lookup")]
		[field: SerializeField] public int AmbientGroundWidth { get; private set; } = 32;
		[field: SerializeField] public int AmbientGroundSamples { get; private set; } = 64;

		[field: Header("Ambient Sky Lookup")]
		[field: SerializeField] public int AmbientSkyWidth { get; private set; } = 128;
		[field: SerializeField] public int AmbientSkyHeight { get; private set; } = 64;
		[field: SerializeField] public int AmbientSkySamples { get; private set; } = 64;

		[field: Header("Reflection Probe")]
		[field: SerializeField] public int ReflectionResolution { get; private set; } = 128;
		[field: SerializeField] public int ReflectionSamples { get; private set; } = 16;

		[field: Header("Rendering")]
		[field: SerializeField] public int RenderSamples { get; private set; } = 32;

		[field: Header("Convolution")]
		[field: SerializeField] public int ConvolutionSamples { get; private set; } = 64;

		[field: Header("Temporal")]
		[field: SerializeField, Range(0, 32)] public int MaxFrameCount { get; private set; } = 16;
		[field: SerializeField, Range(0.0f, 1.0f)] public float StationaryBlend { get; private set; } = 0.95f;
		[field: SerializeField, Range(0.0f, 1.0f)] public float MotionBlend { get; private set; } = 0.0f;
		[field: SerializeField, Min(0.0f)] public float MotionFactor { get; private set; } = 6000.0f;
		[field: SerializeField, Range(0.0f, 2.0f)] public float DepthFactor { get; private set; } = 0.0f;
		[field: SerializeField, Range(0.0f, 2.0f)] public float ClampWindow { get; private set; } = 1.0f;

		[field: Header("Spatial")]
		[field: SerializeField, Range(0, 32)] public int SpatialSamples { get; private set; } = 4;
		[field: SerializeField, Min(0.0f)] public float SpatialDepthFactor { get; private set; } = 0.1f;
		[field: SerializeField, Min(0.0f)] public float SpatialBlurSigma { get; private set; } = 0.1f;
		[field: SerializeField, Range(0, 32)] public int SpatialBlurFrames { get; private set; } = 8;
		[field: SerializeField] public Texture2D miePhase;
		[field: NonSerialized] public int Version { get; private set; }
	}
}