using System;
using UnityEngine;

public partial class UIBlur
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField, Range(0, 32)] public int BlurRadius { get; private set; } = 16;
        [field: SerializeField] public float BlurSigma { get; private set; } = 16.0f;
    }
}