using System;
using UnityEngine;

[Serializable]
public class WhiteBalanceSettings
{
    [field: SerializeField, Range(-100, 100)] public float Temperature { get; private set; } = 0.0f;
    [field: SerializeField, Range(-100, 100)] public float Tint { get; private set; } = 0.0f;
}
