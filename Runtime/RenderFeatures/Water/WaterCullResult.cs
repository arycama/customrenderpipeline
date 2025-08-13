using UnityEngine;

public readonly struct WaterCullResult
{
    public ResourceHandle<GraphicsBuffer> IndirectArgsBuffer { get; }
    public ResourceHandle<GraphicsBuffer> PatchDataBuffer { get; }

    public WaterCullResult(ResourceHandle<GraphicsBuffer> indirectArgsBuffer, ResourceHandle<GraphicsBuffer> patchDataBuffer)
    {
        IndirectArgsBuffer = indirectArgsBuffer;
        PatchDataBuffer = patchDataBuffer;
    }
}