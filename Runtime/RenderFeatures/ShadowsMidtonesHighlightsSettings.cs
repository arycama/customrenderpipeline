using System;
using UnityEngine;

[Serializable]
public class ShadowsMidtonesHighlightsSettings
{
    [field: SerializeField, ColorUsage(false, true)] public Color Shadows { get; private set; } = Color.white;
    [field: SerializeField, ColorUsage(false, true)] public Color Midtones { get; private set; } = Color.white;
    [field: SerializeField, ColorUsage(false, true)] public Color Highlights { get; private set; } = Color.white;

    [field: SerializeField, Range(0f, 2f)] public float ShadowsStart { get; private set; } = 0.0f;
    [field: SerializeField, Range(0f, 2f)] public float ShadowsEnd { get; private set; } = 0.3f;
    [field: SerializeField, Range(0f, 2f)] public float HighlightsStart { get; private set; } = 0.55f;
    [field: SerializeField, Range(0f, 2f)] public float HighlightsEnd { get; private set; } = 1.0f;
}
