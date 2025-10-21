using UnityEngine;
using UnityEngine.Rendering;

public readonly struct ViewData : IRenderPassData
{
	private readonly ResourceHandle<GraphicsBuffer> buffer;

	public ViewData(ResourceHandle<GraphicsBuffer> buffer)
	{
		this.buffer = buffer;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadBuffer("ViewData", buffer);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}
