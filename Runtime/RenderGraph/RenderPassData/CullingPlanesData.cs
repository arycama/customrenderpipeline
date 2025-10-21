using UnityEngine.Rendering;

public readonly struct CullingPlanesData : IRenderPassData
{
	public readonly CullingPlanes cullingPlanes;

	public CullingPlanesData(CullingPlanes cullingPlanes) => this.cullingPlanes = cullingPlanes;

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}