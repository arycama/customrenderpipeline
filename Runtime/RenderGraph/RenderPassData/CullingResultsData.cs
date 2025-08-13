using UnityEngine.Rendering;

public class CullingResultsData : IRenderPassData
{
	public CullingResultsData(CullingResults cullingResults) => CullingResults = cullingResults;

	public CullingResults CullingResults { get; }

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{

	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}
