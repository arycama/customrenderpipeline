using UnityEngine;
using UnityEngine.Rendering;

public readonly struct TemporalAAData : IRenderPassData
{
	private readonly ResourceHandle<GraphicsBuffer> buffer;

	public TemporalAAData(ResourceHandle<GraphicsBuffer> buffer)
	{
		this.buffer = buffer;
	}

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadBuffer("TemporalProperties", buffer);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}