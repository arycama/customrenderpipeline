using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public partial class LitData
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField] public int DirectionalAlbedoResolution { get; private set; } = 32;
            [field: SerializeField] public uint DirectionalAlbedoSamples { get; private set; } = 4096;
            [field: SerializeField] public int AverageAlbedoResolution { get; private set; } = 16;
            [field: SerializeField] public uint AverageAlbedoSamples { get; private set; } = 4096;
            [field: SerializeField] public int DirectionalAlbedoMsResolution { get; private set; } = 16;
            [field: SerializeField] public uint DirectionalAlbedoMSamples { get; private set; } = 4096;
            [field: SerializeField] public int AverageAlbedoMsResolution { get; private set; } = 16;
            [field: SerializeField] public uint AverageAlbedoMsSamples { get; private set; } = 4096;

            public int Version { get; private set; }
        }
    }
}