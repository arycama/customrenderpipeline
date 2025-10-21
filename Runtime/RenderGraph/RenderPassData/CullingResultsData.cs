using UnityEngine.Rendering;

public readonly struct CullingResultsData : IRenderPassData
{
	public readonly CullingResults cullingResults;

	public CullingResultsData(CullingResults cullingResults) => this.cullingResults = cullingResults;

	void IRenderPassData.SetInputs(RenderPass pass)
	{

	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}
