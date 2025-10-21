using UnityEngine;
using UnityEngine.Rendering;

public readonly struct TerrainSystemData : IRenderPassData
{
	public readonly ResourceHandle<RenderTexture> minMaxHeights;
	public readonly Terrain terrain;
	public readonly TerrainData terrainData;
	public readonly ResourceHandle<GraphicsBuffer> indexBuffer;

	public TerrainSystemData(ResourceHandle<RenderTexture> minMaxHeights, Terrain terrain, TerrainData terrainData, ResourceHandle<GraphicsBuffer> indexBuffer)
	{
		this.minMaxHeights = minMaxHeights;
		this.terrain = terrain;
		this.terrainData = terrainData;
		this.indexBuffer = indexBuffer;
	}

	void IRenderPassData.SetInputs(RenderPass pass)
	{
		pass.ReadBuffer("", indexBuffer);
	}

	void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
	{
	}
}