using UnityEngine.Rendering;

public readonly struct CullingPlanesData : IRenderPassData
{
	public readonly CullingPlanes cullingPlanes;

	public CullingPlanesData(CullingPlanes cullingPlanes) => this.cullingPlanes = cullingPlanes;

	void IRenderPassData.SetInputs(RenderPass pass)
	{
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}