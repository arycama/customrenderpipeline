using System;
using UnityEngine;

[Serializable]
public class EnvironmentLightingSettings
{
	[field: SerializeField, Range(1, 512)] public int Resolution { get; private set; } = 128;
	[field: SerializeField, Range(1, 512)] public int Samples { get; private set; } = 128;
}
