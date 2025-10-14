using UnityEngine;
using UnityEngine.Rendering;

public class LightingData : IRenderPassData
{
	public Float3 Light0Direction { get; }
	public Float3 Light0Color { get; }
	public Float3 Light1Direction { get; }
	public Float3 Light1Color { get; }
	private readonly ResourceHandle<GraphicsBuffer> lightingData;
	private readonly ResourceHandle<GraphicsBuffer> directionalShadowMatrices;
	private readonly ResourceHandle<GraphicsBuffer> directionalCascadeSizes;

	public LightingData(Float3 light0Direction, Float3 light0Color, Float3 light1Direction, Float3 light1Color, ResourceHandle<GraphicsBuffer> lightingData, ResourceHandle<GraphicsBuffer> directionalShadowMatrices, ResourceHandle<GraphicsBuffer> directionalCascadeSizes)
	{
		Light0Direction = light0Direction;
		Light0Color = light0Color;
		Light1Direction = light1Direction;
		Light1Color = light1Color;
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