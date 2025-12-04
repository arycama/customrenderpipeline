using UnityEngine.Rendering;

public readonly struct ViewInfo : IRenderPassData
{
	public readonly Int2 viewSize;

    public ViewInfo(Int2 viewSize) => this.viewSize = viewSize;

    void IRenderPassData.SetInputs(RenderPass pass)
    {
    }

    void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
    {
    }
}