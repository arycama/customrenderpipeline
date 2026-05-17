using UnityEngine;
using UnityEngine.Rendering;

public readonly struct GizmosTarget : IRenderPassData
{
    private readonly ResourceHandle<RenderTexture> gizmosTarget;

    public GizmosTarget(ResourceHandle<RenderTexture> gizmosTarget)
    {
        this.gizmosTarget = gizmosTarget;
    }

    void IRenderPassData.SetInputs(RenderPass pass)
    {
        pass.ReadTexture("GizmosTarget", gizmosTarget);
    }

    void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
    {
    }
}
