using System;
using UnityEngine;

[Serializable]
public class LightingSettings
{
	[field: SerializeField] public int EnvironmentResolution { get; private set; } = 128;
	[field: SerializeField, Range(1, 512)] public int EnvironmentSamples { get; private set; } = 128;
	[field: SerializeField] public bool MicroShadows { get; private set; } = true;
	[field: SerializeField, Range(1e-3f, 180)] public float SunAngularDiameter { get; private set; } = 0.52f;

	[field: SerializeField, Range(0, 0.4999f)] public float SpaceWastingTolerance { get; private set; } = 0.25f;
	[field: SerializeField, Range(1, 8)] public int MaxLengthPartitions { get; private set; } = 4;
	[field: SerializeField, Range(1, 4)] public int MaxTransverseParitions { get; private set; } = 2;
	[field: SerializeField, Min(0)] public float FootprintTolerance { get; private set; } = 0.25f;
	[field: SerializeField] public float MaxTheta { get; private set; } = Math.HalfPi;

	[field: SerializeField] public bool SnapTexels { get; private set; } = true;
	[field: SerializeField] public bool UseCloseFit { get; private set; } = true;
	[field: SerializeField] public bool UseOverlapFix { get; private set; } = true;
	[field: SerializeField, Range(0, 1)] public float CascadeUniformity { get; private set; } = 0.5f;
	[field: SerializeField, Range(1, 8)] public int DirectionalCascadeCount { get; private set; } = 4;
	[field: SerializeField] public float DirectionalShadowDistance { get; private set; } = 128;
	[field: SerializeField] public int DirectionalShadowResolution { get; private set; } = 4096;
	[field: SerializeField] public float DirectionalShadowBias { get; private set; } = 5;
	[field: SerializeField] public float DirectionalShadowSlopeBias { get; private set; } = 1;

	[field: SerializeField] public int PointShadowResolution { get; private set; } = 256;
	[field: SerializeField] public float PointShadowBias { get; private set; } = 0.0f;
	[field: SerializeField] public float PointShadowSlopeBias { get; private set; } = 0.0f;

	[field: SerializeField] public int SpotShadowResolution { get; private set; } = 512;
	[field: SerializeField] public float SpotShadowBias { get; private set; } = 0.0f;
	[field: SerializeField] public float SpotShadowSlopeBias { get; private set; } = 0.0f;
}