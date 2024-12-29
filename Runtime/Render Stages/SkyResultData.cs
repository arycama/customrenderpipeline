using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct SkyResultData : IRenderPassData
{
    public ResourceHandle<RenderTexture> SkyTexture { get; }

    public SkyResultData(ResourceHandle<RenderTexture> skyTexture)
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
