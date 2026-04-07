using System;
using UnityEngine;

[Serializable]
public class EnvironmentLightingSettings
{
    [field: SerializeField] public bool ForceUpdate { get; private set; } = false;
	[field: SerializeField, Pow2(2048)] public int Resolution { get; private set; } = 128;
	[field: SerializeField, Pow2(512)] public int Samples { get; private set; } = 128;
}
