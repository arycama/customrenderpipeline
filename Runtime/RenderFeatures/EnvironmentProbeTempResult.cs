using UnityEngine;
using UnityEngine.Rendering;

public readonly struct EnvironmentProbeTempResult : IRenderPassData
{
	public ResourceHandle<RenderTexture> TempProbe { get; }

	public EnvironmentProbeTempResult(ResourceHandle<RenderTexture> tempProbe) => TempProbe = tempProbe;

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadTexture("Input", TempProbe);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}