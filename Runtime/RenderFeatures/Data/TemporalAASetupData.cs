using UnityEngine;
using UnityEngine.Rendering;

public readonly struct TemporalAASetupData : IRenderPassData
{
	public readonly Float2 jitter;

    public TemporalAASetupData(Float2 jitter)
    {
        this.jitter = jitter;
    }

	void IRenderPassData.SetInputs(RenderPass pass)
	{
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}