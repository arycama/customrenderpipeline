using UnityEngine;

public readonly struct TerrainCullResult
{
	public ResourceHandle<GraphicsBuffer> IndirectArgsBuffer { get; }
	public ResourceHandle<GraphicsBuffer> PatchDataBuffer { get; }

	public TerrainCullResult(ResourceHandle<GraphicsBuffer> indirectArgsBuffer, ResourceHandle<GraphicsBuffer> patchDataBuffer)
	{
		IndirectArgsBuffer = indirectArgsBuffer;
		PatchDataBuffer = patchDataBuffer;
	}
}