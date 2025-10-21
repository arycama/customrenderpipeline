using UnityEngine;
using UnityEngine.Rendering;

public readonly struct LightingData : IRenderPassData
{
	public readonly Float3 light0Direction;
	public readonly Float3 light0Color;
	public readonly Float3 light1Direction;
	public readonly Float3 light1Color;
	private readonly ResourceHandle<GraphicsBuffer> lightingData;
	private readonly ResourceHandle<GraphicsBuffer> directionalShadowMatrices;
	private readonly ResourceHandle<GraphicsBuffer> directionalCascadeSizes;

	public LightingData(Float3 light0Direction, Float3 light0Color, Float3 light1Direction, Float3 light1Color, ResourceHandle<GraphicsBuffer> lightingData, ResourceHandle<GraphicsBuffer> directionalShadowMatrices, ResourceHandle<GraphicsBuffer> directionalCascadeSizes)
	{
		this.light0Direction = light0Direction;
		this.light0Color = light0Color;
		this.light1Direction = light1Direction;
		this.light1Color = light1Color;
		this.lightingData = lightingData;
		this.directionalShadowMatrices = directionalShadowMatrices;
		this.directionalCascadeSizes = directionalCascadeSizes;
	}

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadBuffer("LightingData", lightingData);
		pass.ReadBuffer("DirectionalShadowMatrices", directionalShadowMatrices);
		pass.ReadBuffer("DirectionalCascadeSizes", directionalCascadeSizes);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}