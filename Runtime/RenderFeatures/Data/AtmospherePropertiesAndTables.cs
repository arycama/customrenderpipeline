using UnityEngine;
using UnityEngine.Rendering;

public struct AtmospherePropertiesAndTables : IRenderPassData
{
	private readonly ResourceHandle<GraphicsBuffer> atmospherePropertiesBuffer;
	private readonly ResourceHandle<RenderTexture> transmittance;
	private readonly ResourceHandle<RenderTexture> multiScatter;
	private readonly ResourceHandle<RenderTexture> groundAmbient;
	private readonly ResourceHandle<RenderTexture> skyAmbient;

	private Float4 transmittanceRemap;
	private Float4 multiScatterRemap;
	private Float4 skyAmbientRemap;
	private Float2 groundAmbientRemap;

	public AtmospherePropertiesAndTables(ResourceHandle<GraphicsBuffer> atmospherePropertiesBuffer, ResourceHandle<RenderTexture> transmittance, ResourceHandle<RenderTexture> multiScatter, ResourceHandle<RenderTexture> groundAmbient, ResourceHandle<RenderTexture> skyAmbient, Float4 transmittanceRemap, Float4 multiScatterRemap, Float4 skyAmbientRemap, Float2 groundAmbientRemap)
	{
		this.atmospherePropertiesBuffer = atmospherePropertiesBuffer;
		this.transmittance = transmittance;
		this.multiScatter = multiScatter;
		this.groundAmbient = groundAmbient;
		this.skyAmbient = skyAmbient;
		this.transmittanceRemap = transmittanceRemap;
		this.multiScatterRemap = multiScatterRemap;
		this.skyAmbientRemap = skyAmbientRemap;
		this.groundAmbientRemap = groundAmbientRemap;
	}

	public readonly void SetInputs(RenderPass pass)
	{
		pass.ReadBuffer("AtmosphereProperties", atmospherePropertiesBuffer);
		pass.ReadTexture("SkyTransmittance", transmittance);
		pass.ReadTexture("_MultiScatter", multiScatter);
		pass.ReadTexture("_SkyAmbient", skyAmbient);
		pass.ReadTexture("_GroundAmbient", groundAmbient);
	}

	public readonly void SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetVector("SkyTransmittanceRemap", transmittanceRemap);
		pass.SetVector("_MultiScatterRemap", multiScatterRemap);
		pass.SetVector("_SkyAmbientRemap", skyAmbientRemap);
		pass.SetVector("_GroundAmbientRemap", groundAmbientRemap);
	}
}