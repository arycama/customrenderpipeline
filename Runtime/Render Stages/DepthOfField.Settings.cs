using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public partial class DepthOfField
    {
        [Serializable]
        public class Settings
        {
            [SerializeField, Min(0f)] private float sampleRadius = 8f;
            [SerializeField, Range(1, 128)] private int sampleCount = 8;

            public float SampleRadius => sampleRadius;
            public int SampleCount => sampleCount;
        }
    }
}