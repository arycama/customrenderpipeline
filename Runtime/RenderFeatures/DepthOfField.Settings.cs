using System;
using UnityEngine;

public partial class DepthOfField
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField] public bool IsEnabled { get; private set; } = true;
		[field: SerializeField, Range(1, 128)] public int SampleCount { get; private set; } = 8;
        [field: SerializeField, Min(0)] public float FocalDistance { get; private set; } = 1f;
        [field: SerializeField] public bool UseRaytracing { get; private set; } = false;
	}
}