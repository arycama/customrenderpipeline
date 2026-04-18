using UnityEngine;
using UnityEngine.Rendering;

public readonly struct LightingData : IRenderPassData
{
	public readonly Quaternion light0Rotation;
	public readonly Float3 light0Color;
	public readonly Quaternion light1Rotation;
	public readonly Float3 light1Color;
	private readonly ResourceHandle<GraphicsBuffer> lightingData;
	private readonly ResourceHandle<GraphicsBuffer> directionalShadowMatrices;
	private readonly ResourceHandle<GraphicsBuffer> directionalCascadeSizes;

	public LightingData(Quaternion light0Rotation, Float3 light0Color, Quaternion light1Rotation, Float3 light1Color, ResourceHandle<GraphicsBuffer> lightingData, ResourceHandle<GraphicsBuffer> directionalShadowMatrices, ResourceHandle<GraphicsBuffer> directionalCascadeSizes)
	{
		this.light0Rotation = light0Rotation;
		this.light0Color = light0Color;
		this.light1Rotation = light1Rotation;
		this.light1Color = light1Color;
		this.lightingData = lightingData;
		this.directionalShadowMatrices = directionalShadowMatrices;
		this.directionalCascadeSizes = directionalCascadeSizes;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadBuffer("LightingData", lightingData);
		pass.ReadBuffer("DirectionalShadowMatrices", directionalShadowMatrices);
		pass.ReadBuffer("DirectionalCascadeSizes", directionalCascadeSizes);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}