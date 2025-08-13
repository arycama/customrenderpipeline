using System;
using UnityEngine;

public partial class VolumetricLighting
{
	[Serializable]
    public class Settings
    {
        [field: SerializeField] public int TileSize { get; private set; } = 8;
        [field: SerializeField] public int DepthSlices { get; private set; } = 128;
        [field: SerializeField, Range(0.0f, 2.0f)] public float BlurSigma { get; private set; } = 1.0f;
        [field: SerializeField] public float MaxDistance { get; private set; } = 512.0f;
    }
}