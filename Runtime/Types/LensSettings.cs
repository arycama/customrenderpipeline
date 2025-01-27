using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    [Serializable]
    public class LensSettings
    {
        [field: SerializeField] public float FocalDistance { get; private set; } = 10f;
        [field: SerializeField, Range(0.0f, 1.0f)] public float DefocusAngle { get; private set; } = 0.5f;
        [field: SerializeField] public float ShutterSpeed { get; private set; } = 200.0f;
        [field: SerializeField] public float Iso { get; private set; } = 200f;
        [field: SerializeField, Min(0.0f)] public float Aperture { get; private set; } = 11.0f;
        [field: SerializeField] public float SensorSize { get; private set; } = 24.89f;
    }
}