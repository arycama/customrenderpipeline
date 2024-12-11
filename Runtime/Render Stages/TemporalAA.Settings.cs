using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public partial class TemporalAA
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField] public bool IsEnabled { get; private set; } = true;
            [field: SerializeField, Range(0.0f, 1.0f)] public float JitterSpread { get; private set; } = 1.0f;
            [field: SerializeField, Range(0f, 2f)] public float SpatialSharpness { get; private set; } = 0.5f;
            [field: SerializeField, Range(0f, 1f)] public float MotionSharpness { get; private set; } = 0.5f;
            [field: SerializeField, Range(0f, 0.99f)] public float StationaryBlending { get; private set; } = 0.95f;
            [field: SerializeField, Range(0f, 0.99f)] public float MotionBlending { get; private set; } = 0.85f;
            [field: SerializeField, Range(1, 32)] public int SampleCount { get; private set; } = 8;
            [field: SerializeField] public float MotionWeight { get; private set; } = 6000f;
            [field: SerializeField] public bool JitterOverride { get; private set; } = false;
            [field: SerializeField] public Vector2 JitterOverrideValue { get; private set; } = Vector2.zero;
        }
    }
}