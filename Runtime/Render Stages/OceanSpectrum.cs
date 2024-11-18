using System;
using UnityEngine;

[Serializable]
public struct OceanSpectrum
{
    [field: SerializeField, Range(0, 64)] public float WindSpeed { get; private set; }
    [field: SerializeField, Range(0f, 1f)] public float WindAngle { get; private set; }
    [field: SerializeField, Min(0f)] public float Fetch { get; private set; }
    [field: SerializeField, Range(0, 1)] public float SpreadBlend { get; private set; }
    [field: SerializeField, Range(0, 1)] public float Swell { get; private set; }
    [field: SerializeField, Min(1e-6f)] public float PeakEnhancement { get; private set; }
    [field: SerializeField, Range(0, 2f)] public float ShortWavesFade { get; private set; }

    public OceanSpectrum(float windSpeed, float windAngle, float fetch, float spreadBlend, float swell, float peakEnhancement, float shortWavesFade)
    {
        WindSpeed = windSpeed;
        WindAngle = windAngle;
        Fetch = fetch;
        SpreadBlend = spreadBlend;
        Swell = swell;
        PeakEnhancement = peakEnhancement;
        ShortWavesFade = shortWavesFade;
    }
}