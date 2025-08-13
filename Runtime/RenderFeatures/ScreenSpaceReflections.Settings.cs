using System;
using UnityEngine;

public partial class ScreenSpaceReflections
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField, Range(0.0f, 1.0f)] public float Intensity { get; private set; } = 1.0f;
        [field: SerializeField, Range(1, 128)] public int MaxSamples { get; private set; } = 32;
        [field: SerializeField, Range(0f, 1.0f), Tooltip("Thickness of a Depth Buffer Sample in world units")] public float Thickness { get; private set; } = 1.0f;
        [field: SerializeField, Range(0, 32)] public int ResolveSamples { get; private set; } = 8;
        [field: SerializeField, Min(0.0f)] public float ResolveSize { get; private set; } = 16.0f;
        [field: SerializeField] public bool UseRaytracing { get; private set; } = true;
    }
}