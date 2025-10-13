using UnityEngine;
using UnityEngine.Rendering;

public readonly struct EnvironmentProbeTempResult : IRenderPassData
{
	public ResourceHandle<RenderTexture> TempProbe { get; }

	public EnvironmentProbeTempResult(ResourceHandle<RenderTexture> tempProbe) => this.TempProbe = tempProbe;

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("_AmbientProbeInputCubemap", TempProbe);
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}