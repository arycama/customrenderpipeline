using UnityEngine;
using UnityEngine.Rendering;

public readonly struct EnvironmentData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> skyReflection;
	private readonly ResourceHandle<GraphicsBuffer> ambientBuffer;

	public EnvironmentData(ResourceHandle<RenderTexture> skyReflection, ResourceHandle<GraphicsBuffer> ambientBuffer)
	{
		this.skyReflection = skyReflection;
		this.ambientBuffer = ambientBuffer;
	}

    readonly void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("SkyReflection", skyReflection);
		pass.ReadBuffer("AmbientShBuffer", ambientBuffer);
	}

    readonly void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}