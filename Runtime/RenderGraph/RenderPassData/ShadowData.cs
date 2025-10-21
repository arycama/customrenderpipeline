using UnityEngine;
using UnityEngine.Rendering;

public struct ShadowData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> directionalShadows;
    private readonly ResourceHandle<RenderTexture> pointShadows;
    private readonly ResourceHandle<RenderTexture> spotShadows;

	public ShadowData(ResourceHandle<RenderTexture> directionalShadows, ResourceHandle<RenderTexture> pointShadows, ResourceHandle<RenderTexture> spotShadows)
	{
		this.directionalShadows = directionalShadows;
		this.pointShadows = pointShadows;
		this.spotShadows = spotShadows;
	}

	public void SetInputs(RenderPass pass)
	{
		pass.ReadTexture("DirectionalShadows", directionalShadows);
		pass.ReadTexture("PointShadows", pointShadows);
		pass.ReadTexture("SpotShadows", spotShadows);
	}

	public void SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}
