using UnityEngine;
using UnityEngine.Rendering;

public readonly struct HdrOutputData : IRenderPassData
{
    public readonly HDROutputSettings settings;
    public readonly ColorGamut colorGamut;
    public readonly float peakLuminance;
    public readonly bool hdrAvailable;

    public HdrOutputData(ColorGamut colorGamut, float peakLuminance, bool hdrAvailable, HDROutputSettings settings)
    {
        this.colorGamut = colorGamut;
        this.peakLuminance = peakLuminance;
        this.hdrAvailable = hdrAvailable;
        this.settings = settings;
    }

    void IRenderPassData.SetInputs(RenderPass pass)
    {
    }

    void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
    {
    }
}