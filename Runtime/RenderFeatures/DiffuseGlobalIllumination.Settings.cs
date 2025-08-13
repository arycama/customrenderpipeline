using System;
using UnityEngine;

public partial class DiffuseGlobalIllumination
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField, Range(0.0f, 1.0f)] public float Intensity { get; private set; } = 1.0f;
		[field: SerializeField, Range(1, 128)] public int MaxSamples { get; private set; } = 32;
		[field: SerializeField, Range(0f, 1.0f)] public float Thickness { get; private set; } = 0.1f;
		[field: SerializeField, Range(0.0f, 179.0f)] public float ConeAngle { get; private set; } = (1.0f / Mathf.PI) * Mathf.Rad2Deg;
		[field: SerializeField, Range(0, 32)] public int ResolveSamples { get; private set; } = 8;
		[field: SerializeField, Min(0.0f)] public float ResolveSize { get; private set; } = 16.0f;
		[field: SerializeField] public bool UseRaytracing { get; private set; } = true;
	}
}