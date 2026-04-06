using System;
using UnityEngine;

[Serializable]
public class WhiteBalanceSettings
{
    [field: SerializeField] public bool UseColorTemperature { get; private set; } = false;
    [field: SerializeField] public float ColorTemperature { get; private set; } = 6500f;
    [field: SerializeField, Range(-100, 100)] public float Temperature { get; private set; } = 0.0f;
    [field: SerializeField, Range(-100, 100)] public float Tint { get; private set; } = 0.0f;
}
