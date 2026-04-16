using UnityEngine;
using UnityEngine.Rendering;

public readonly struct SkyViewTransmittanceData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> skyTransmittance;
    private readonly Float4 skyViewTransmittanceRemap;

	public SkyViewTransmittanceData(ResourceHandle<RenderTexture> skyTransmittance, Float4 skyViewTransmittanceRemap)
	{
		this.skyTransmittance = skyTransmittance;
		this.skyViewTransmittanceRemap = skyViewTransmittanceRemap;
	}

	public void SetInputs(RenderPass pass)
	{
		pass.ReadTexture("SkyViewTransmittance", skyTransmittance);
	}

	public void SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetVector("SkyViewTransmittanceRemap", skyViewTransmittanceRemap);
	}
}