using UnityEngine;
using UnityEngine.Rendering;

public struct SkyReflectionAmbientData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> skyCdf;
	private readonly ResourceHandle<RenderTexture> skyLuminance;
	private readonly ResourceHandle<RenderTexture> weightedDepth;
    private Float4 skyLuminanceRemap, skyCdfRemap;

	public SkyReflectionAmbientData(ResourceHandle<RenderTexture> skyCdf, ResourceHandle<RenderTexture> skyLuminance, ResourceHandle<RenderTexture> weightedDepth, Float4 skyLuminanceRemap, Float4 skyCdfRemap)
	{
		this.skyCdf = skyCdf;
		this.skyLuminance = skyLuminance;
		this.weightedDepth = weightedDepth;
		this.skyLuminanceRemap = skyLuminanceRemap;
		this.skyCdfRemap = skyCdfRemap;
	}

	public readonly void SetInputs(RenderPass pass)
	{
		pass.ReadTexture("SkyCdf", skyCdf);
		pass.ReadTexture("SkyLuminance", skyLuminance);
		pass.ReadTexture("AtmosphereDepth", weightedDepth);
	}

	public readonly void SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetVector("SkyLuminanceRemap", skyLuminanceRemap);
		pass.SetVector("SkyCdfRemap", skyCdfRemap);
	}
}