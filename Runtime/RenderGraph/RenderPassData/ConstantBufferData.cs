using UnityEngine;
using UnityEngine.Rendering;

public abstract class ConstantBufferData : IRenderPassData
{
	private readonly ResourceHandle<GraphicsBuffer> buffer;
	private readonly string propertyName;

	public ConstantBufferData(ResourceHandle<GraphicsBuffer> buffer, string propertyName)
	{
		this.buffer = buffer;
		this.propertyName = propertyName;
	}

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadBuffer(propertyName, buffer);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}
