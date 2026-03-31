using System;
using UnityEngine;

[Serializable]
public class ColorAdjustmentsSettings
{
    [field: SerializeField] public float PostExposure { get; private set; } = 0.0f;
    [field: SerializeField, Range(-100, 100)] public float Contrast { get; private set; } = 0.0f;
    [field: SerializeField, ColorUsage(false)] public Color ColorFilter { get; private set; } = Color.white;
    [field: SerializeField, Range(-180, 180)] public float HueShift { get; private set; } = 0.0f;
    [field: SerializeField, Range(-100, 100)] public float Saturation { get; private set; } = 0.0f;
}
