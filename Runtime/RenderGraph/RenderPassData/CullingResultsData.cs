using UnityEngine.Rendering;

public readonly struct CullingResultsData : IRenderPassData
{
	public readonly CullingResults cullingResults;

	public CullingResultsData(CullingResults cullingResults) => this.cullingResults = cullingResults;

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{

	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}
