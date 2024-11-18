using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Data/Graphics/Water Profile New")]
public class WaterProfile : ScriptableObject
{
    [field: SerializeField, Tooltip("Gravity, affects total size and height of waves")] public float Gravity { get; private set; } = 9.81f;
    [field: SerializeField, Tooltip("The size in world units of the simulated patch. Larger values spread waves out, and create bigger waves")] public float PatchSize { get; private set; } = 2048;
    [field: SerializeField] public float CascadeScale { get; private set; } = 5.23f;
    [field: SerializeField, Range(0f, 2f)] public float FoamThreshold { get; private set; } = 0.5f;
    [field: SerializeField, Range(0f, 1f)] public float FoamStrength { get; private set; } = 0.5f;
    [field: SerializeField, Range(0f, 1f)] public float FoamDecay { get; private set; } = 0.85f;
    [field: SerializeField] public float MaxWaterHeight { get; private set; } = 32f;
    [field: SerializeField] public float TimeScale { get; private set; } = 1.0f;
    [field: SerializeField] public float SequenceLength { get; private set; } = 200.0f;
    [field: SerializeField] public float MaxWaveNumberMultiplier { get; private set; } = 10.0f;

    [field: Header("Spectrum")]
    [field: SerializeField, Range(0, 64)] public float WindSpeed { get; private set; }
    [field: SerializeField, Range(0f, 1f)] public float WindAngle { get; private set; }
    [field: SerializeField, Min(0f)] public float Fetch { get; private set; }
    [field: SerializeField, Range(0, 1)] public float SpreadBlend { get; private set; }
    [field: SerializeField, Range(0, 1)] public float Swell { get; private set; }
    [field: SerializeField, Min(1e-6f)] public float PeakEnhancement { get; private set; }
    [field: SerializeField, Range(0, 2f)] public float ShortWavesFade { get; private set; }
    public float MaxWaveNumber => CascadeScale * MaxWaveNumberMultiplier;
}
