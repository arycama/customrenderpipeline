using UnityEngine;
using UnityEngine.Rendering;

public readonly struct FrameData : IRenderPassData
{
	private readonly ResourceHandle<GraphicsBuffer> buffer;

	public FrameData(ResourceHandle<GraphicsBuffer> buffer)
	{
		this.buffer = buffer;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadBuffer("FrameData", buffer);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}