using UnityEngine;
using UnityEngine.Rendering;

public readonly struct TerrainViewData : IRenderPassData
{
	private readonly ResourceHandle<GraphicsBuffer> terrainViewData;

	public TerrainViewData(ResourceHandle<GraphicsBuffer> terrainViewData)
	{
		this.terrainViewData = terrainViewData;
	}

	public readonly void SetInputs(RenderPass pass)
	{
		pass.ReadBuffer("TerrainViewData", terrainViewData);
	}

	public readonly void SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}