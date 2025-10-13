using UnityEngine;
using UnityEngine.Rendering;

public class DfgData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> precomputeDfg, directionalAlbedo, averageAlbedo, directionalAlbedoMs, averageAlbedoMs, specularOcclusion;

	public DfgData(ResourceHandle<RenderTexture> precomputeDfg, ResourceHandle<RenderTexture> directionalAlbedo, ResourceHandle<RenderTexture> averageAlbedo, ResourceHandle<RenderTexture> directionalAlbedoMs, ResourceHandle<RenderTexture> averageAlbedoMs, ResourceHandle<RenderTexture> specularOcclusion)
	{
		this.precomputeDfg = precomputeDfg;
		this.directionalAlbedo = directionalAlbedo;
		this.averageAlbedo = averageAlbedo;
		this.directionalAlbedoMs = directionalAlbedoMs;
		this.averageAlbedoMs = averageAlbedoMs;
		this.specularOcclusion = specularOcclusion;
	}

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("PrecomputedDfg", precomputeDfg);
		pass.ReadTexture("DirectionalAlbedo", directionalAlbedo);
		pass.ReadTexture("AverageAlbedo", averageAlbedo);
		pass.ReadTexture("DirectionalAlbedoMs", directionalAlbedoMs);
		pass.ReadTexture("AverageAlbedoMs", averageAlbedoMs);
		pass.ReadTexture("SpecularOcclusion", specularOcclusion);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}