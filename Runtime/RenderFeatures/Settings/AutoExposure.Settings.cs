using System;
using UnityEngine;

public partial class AutoExposure
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField] public bool DebugExposure { get; private set; } = false;
		[field: SerializeField] public ExposureMode ExposureMode { get; private set; } = ExposureMode.Automatic;
		[field: SerializeField] public float AdaptationSpeed { get; private set; } = 1.1f;
		[field: SerializeField, Range(0.0f, 100.0f)] public float HistogramMin { get; private set; } = 40.0f;
		[field: SerializeField, Range(0.0f, 100.0f)] public float HistogramMax { get; private set; } = 90.0f;
		[field: SerializeField] public float MinEv { get; private set; } = -10f;
		[field: SerializeField] public float MaxEv { get; private set; } = 18f;

		[field: Header("Metering")]
		[field: SerializeField] public MeteringMode MeteringMode { get; private set; } = MeteringMode.Center;
		[field: SerializeField] public Vector2 ProceduralCenter { get; private set; } = new(0.5f, 0.5f);
		[field: SerializeField] public Vector2 ProceduralRadii { get; private set; } = new(0.2f, 0.3f);
		[field: SerializeField, Min(0.0f)] public float ProceduralSoftness { get; private set; } = 0.5f;

		[field: Header("Exposure Compensation")]
		[field: SerializeField] public float ExposureCompensation { get; private set; } = 0.0f;
		[field: SerializeField] public int ExposureResolution { get; private set; } = 128;
		[field: SerializeField] public AnimationCurve ExposureCurve { get; private set; } = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 1.0f);
	}
}