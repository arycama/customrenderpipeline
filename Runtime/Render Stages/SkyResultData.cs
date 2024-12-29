using Arycama.CustomRenderPipeline;
using System;
using UnityEngine.Rendering;

public readonly struct SkyResultData : IRenderPassData
{
    public RTHandle SkyTexture { get; }

    public SkyResultData(RTHandle skyTexture)
    {
        SkyTexture = skyTexture;
    }

    public readonly void SetInputs(RenderPass pass)
    {
        pass.ReadTexture("SkyTexture", SkyTexture);
    }

    public readonly void SetProperties(RenderPass pass, CommandBuffer command)
    {
        pass.SetVector("SkyTextureScaleLimit", pass.GetScaleLimit2D(SkyTexture));
    }
}
