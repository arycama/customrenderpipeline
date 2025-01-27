using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public partial class DepthOfField
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField, Min(0.0f)] public float MaxCoC { get; private set; } = 1f;
            [field: SerializeField, Range(1, 128)] public int SampleCount { get; private set; } = 8;
        }
    }
}