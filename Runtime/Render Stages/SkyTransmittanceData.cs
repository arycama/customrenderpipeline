using Arycama.CustomRenderPipeline;
using System;
using UnityEngine.Rendering;

public readonly struct SkyTransmittanceData : IRenderPassData
{
    private readonly RTHandle skyTransmittance;
    private readonly int width, height;

    public SkyTransmittanceData(RTHandle skyTransmittance, int width, int height)
    {
        this.skyTransmittance = skyTransmittance ?? throw new ArgumentNullException(nameof(skyTransmittance));
        this.width = width;
        this.height = height;
    }

    public void SetInputs(RenderPass pass)
    {
        pass.ReadTexture("_SkyTransmittance", skyTransmittance);
    }

    public void SetProperties(RenderPass pass, CommandBuffer command)
    {
        pass.SetFloat("_TransmittanceWidth", width);
        pass.SetFloat("_TransmittanceHeight", height);
    }
}