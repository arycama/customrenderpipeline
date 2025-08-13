using UnityEngine;
using UnityEngine.Rendering;

public class EnvironmentData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> skyReflection;
	private readonly ResourceHandle<GraphicsBuffer> ambientBuffer;

	public EnvironmentData(ResourceHandle<RenderTexture> skyReflection, ResourceHandle<GraphicsBuffer> ambientBuffer)
	{
		this.skyReflection = skyReflection;
		this.ambientBuffer = ambientBuffer;
	}

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("_SkyReflection", skyReflection);
		pass.ReadBuffer("AmbientSh", ambientBuffer);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}