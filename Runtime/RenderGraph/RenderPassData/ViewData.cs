using UnityEngine;
using UnityEngine.Rendering;

public readonly struct ViewData : IRenderPassData
{
	private readonly ResourceHandle<GraphicsBuffer> buffer;

	public ViewData(ResourceHandle<GraphicsBuffer> buffer)
	{
		this.buffer = buffer;
	}

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadBuffer("ViewData", buffer);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}
