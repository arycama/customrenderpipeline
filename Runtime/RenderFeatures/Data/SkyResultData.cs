using UnityEngine;
using UnityEngine.Rendering;

public readonly struct SkyResultData : IRenderPassData
{
	public ResourceHandle<RenderTexture> SkyTexture { get; }
	public ResourceHandle<RenderTexture> Stars { get; }

	public SkyResultData(ResourceHandle<RenderTexture> skyTexture, ResourceHandle<RenderTexture> stars)
	{
		SkyTexture = skyTexture;
		Stars = stars;
	}

	public readonly void SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("SkyTexture", SkyTexture);
		pass.ReadTexture("Stars", Stars);
	}

	public readonly void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
		pass.SetVector("SkyTextureScaleLimit", pass.GetScaleLimit2D(SkyTexture));
	}
}
