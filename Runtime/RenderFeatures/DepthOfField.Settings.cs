using System;
using UnityEngine;

public partial class DepthOfField
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField] public bool IsEnabled { get; private set; } = true;
		[field: SerializeField] public bool UseRaytracing { get; private set; } = false;
		[field: SerializeField, Range(1, 128)] public int SampleCount { get; private set; } = 8;
	}
}