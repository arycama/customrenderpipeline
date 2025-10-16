using UnityEngine;
using UnityEngine.Rendering;

public struct ParticleShadowData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> directionalShadows;
	//private readonly ResourceHandle<RenderTexture> pointShadows;
	//private readonly ResourceHandle<RenderTexture> spotShadows;

	public ParticleShadowData(ResourceHandle<RenderTexture> directionalShadows/*, ResourceHandle<RenderTexture> pointShadows, ResourceHandle<RenderTexture> spotShadows*/)
	{
		this.directionalShadows = directionalShadows;
		//this.pointShadows = pointShadows;
		//this.spotShadows = spotShadows;
	}

	public void SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("DirectionalParticleShadows", directionalShadows);
		//pass.ReadTexture("PointShadows", pointShadows);
		//pass.ReadTexture("SpotShadows", spotShadows);
	}

	public void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}