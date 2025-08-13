using UnityEngine;
using UnityEngine.Rendering;

public class TemporalAASetupData : IRenderPassData
{
    public Vector2 Jitter { get; }

    public TemporalAASetupData(Vector2 jitter)
    {
        Jitter = jitter;
    }

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}