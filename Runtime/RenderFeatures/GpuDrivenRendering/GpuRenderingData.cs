using UnityEngine;

public readonly struct GpuRenderingData
{
	public readonly ResourceHandle<GraphicsBuffer> visibilityPredicates;
	public readonly ResourceHandle<GraphicsBuffer> objectToWorld;
	public readonly ResourceHandle<GraphicsBuffer> instanceIdOffsetsBuffer;

	public GpuRenderingData(ResourceHandle<GraphicsBuffer> visibilityPredicates, ResourceHandle<GraphicsBuffer> objectToWorld, ResourceHandle<GraphicsBuffer> instanceIdOffsetsBuffer)
	{
		this.visibilityPredicates = visibilityPredicates;
		this.objectToWorld = objectToWorld;
		this.instanceIdOffsetsBuffer = instanceIdOffsetsBuffer;
	}
}
