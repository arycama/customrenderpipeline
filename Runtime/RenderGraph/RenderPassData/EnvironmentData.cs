using UnityEngine;
using UnityEngine.Rendering;

public readonly struct EnvironmentData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> skyReflection;
	private readonly ResourceHandle<GraphicsBuffer> ambientBuffer;
    private readonly int resolution;

	public EnvironmentData(ResourceHandle<RenderTexture> skyReflection, ResourceHandle<GraphicsBuffer> ambientBuffer, int resolution)
	{
		this.skyReflection = skyReflection;
		this.ambientBuffer = ambientBuffer;
        this.resolution = resolution;
	}

    readonly void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("SkyReflection", skyReflection);
		pass.ReadBuffer("AmbientShBuffer", ambientBuffer);
	}

    readonly void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
        pass.SetFloat("SkyReflectionSize", resolution);
	}
}