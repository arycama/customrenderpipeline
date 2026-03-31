using System;
using UnityEngine;

[Serializable]
public class SplitToningSettings
{
    [field: SerializeField, ColorUsage(false)] public Color Shadows { get; private set; } = Color.gray;
    [field: SerializeField, ColorUsage(false)] public Color Highlights { get; private set; } = Color.gray;
    [field: SerializeField, Range(-100, 100)] public float Balance { get; private set; } = 0.0f;
}
