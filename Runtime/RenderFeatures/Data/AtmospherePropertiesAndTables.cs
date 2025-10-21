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
	private Float2 transmittanceSize;

	public AtmospherePropertiesAndTables(ResourceHandle<GraphicsBuffer> atmospherePropertiesBuffer, ResourceHandle<RenderTexture> transmittance, ResourceHandle<RenderTexture> multiScatter, ResourceHandle<RenderTexture> groundAmbient, ResourceHandle<RenderTexture> skyAmbient, Float4 transmittanceRemap, Float4 multiScatterRemap, Float4 skyAmbientRemap, Float2 groundAmbientRemap, Float2 transmittanceSize)
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
		this.transmittanceSize = transmittanceSize;
	}

	public readonly void SetInputs(RenderPass pass)
	{
		pass.ReadBuffer("AtmosphereProperties", atmospherePropertiesBuffer);
		pass.ReadTexture("_Transmittance", transmittance);
		pass.ReadTexture("_MultiScatter", multiScatter);
		pass.ReadTexture("_SkyAmbient", skyAmbient);
		pass.ReadTexture("_GroundAmbient", groundAmbient);
	}

	public readonly void SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetVector("_AtmosphereTransmittanceRemap", transmittanceRemap);
		pass.SetVector("_MultiScatterRemap", multiScatterRemap);
		pass.SetVector("_SkyAmbientRemap", skyAmbientRemap);
		pass.SetVector("_GroundAmbientRemap", groundAmbientRemap);
		pass.SetVector("_TransmittanceSize", transmittanceSize);
	}
}