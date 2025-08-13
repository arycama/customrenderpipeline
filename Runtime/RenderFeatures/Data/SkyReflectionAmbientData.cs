using UnityEngine;
using UnityEngine.Rendering;

public struct SkyReflectionAmbientData : IRenderPassData
{
	private readonly ResourceHandle<RenderTexture> skyCdf;
	private readonly ResourceHandle<RenderTexture> skyLuminance;
	private readonly ResourceHandle<RenderTexture> weightedDepth;
	private Vector2 skyLuminanceSize;
	private Vector2 cdfLookupSize;

	public SkyReflectionAmbientData(ResourceHandle<RenderTexture> skyCdf, ResourceHandle<RenderTexture> skyLuminance, ResourceHandle<RenderTexture> weightedDepth, Vector2 skyLuminanceSize, Vector2 cdfLookupSize)
	{
		this.skyCdf = skyCdf;
		this.skyLuminance = skyLuminance;
		this.weightedDepth = weightedDepth;
		this.skyLuminanceSize = skyLuminanceSize;
		this.cdfLookupSize = cdfLookupSize;
	}

	public readonly void SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("_SkyCdf", skyCdf);
		pass.ReadTexture("SkyLuminance", skyLuminance);
		pass.ReadTexture("_SkyCdf", skyCdf);
		pass.ReadTexture("_AtmosphereDepth", weightedDepth);
	}

	public readonly void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
		pass.SetVector("SkyLuminanceScaleLimit", pass.GetScaleLimit2D(skyLuminance));
		pass.SetVector("SkyLuminanceSize", skyLuminanceSize);
		pass.SetVector("_SkyCdfSize", cdfLookupSize);
	}
}