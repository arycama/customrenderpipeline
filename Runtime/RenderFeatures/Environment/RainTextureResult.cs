using UnityEngine;
using UnityEngine.Rendering;

public struct RainTextureResult : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> rainTexture;
	private readonly float size;

	public RainTextureResult(ResourceHandle<RenderTexture> rainTexture, float size)
	{
		this.rainTexture = rainTexture;
		this.size = size;
	}

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("RainTexture", rainTexture);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
		pass.SetFloat("RainTextureSize", size);
	}
}
