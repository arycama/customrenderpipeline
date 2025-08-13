using UnityEngine.Rendering;

public class CullingPlanesData : IRenderPassData
{
	public CullingPlanes CullingPlanes { get; }

	public CullingPlanesData(CullingPlanes cullingPlanes) => CullingPlanes = cullingPlanes;

	void IRenderPassData.SetInputs(RenderPassBase pass)
	{
	}

	void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}