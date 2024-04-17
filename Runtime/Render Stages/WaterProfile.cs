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
    [field: SerializeField] public OceanSpectrum LocalSpectrum { get; private set; } = new(1f, 12f, 0f, 1e+5f, 1f, 0.2f, 3.3f, 0.01f);
    [field: SerializeField] public OceanSpectrum DistantSpectrum { get; private set; } = new(0f, 12f, 0f, 1e+5f, 1f, 0.2f, 3.3f, 0.01f);
    [field: SerializeField] public float MaxWaveNumberMultiplier { get; private set; } = 10.0f;
    public float MaxWaveNumber => CascadeScale * MaxWaveNumberMultiplier;

    public BufferHandle SetShaderProperties(RenderGraph renderGraph)
    {
        return renderGraph.SetConstantBuffer(new OceanData(LocalSpectrum, DistantSpectrum));
    }
}

public struct OceanData
{
    public OceanSpectrum spectrum0, spectrum1;

    public OceanData(OceanSpectrum spectrum0, OceanSpectrum spectrum1)
    {
        this.spectrum0 = spectrum0;
        this.spectrum1 = spectrum1;
    }
}
