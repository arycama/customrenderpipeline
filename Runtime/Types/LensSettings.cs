using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    [Serializable]
    public class LensSettings
    {
        [field: SerializeField] public float Iso { get; private set; } = 200f;
        [field: SerializeField] public float ShutterSpeed { get; private set; } = 200.0f;
        [field: SerializeField, Min(0.0f)] public float Aperture { get; private set; } = 16.0f;
        [field: SerializeField] public float SensorSize { get; private set; } = 24.0f;
        [field: SerializeField] public float FocusDistance { get; private set; } = 10f;
    }
}