using UnityEngine;

public struct InstanceRendererData
{
    public GameObject[] GameObjects { get; private set; }
    public ResourceHandle<GraphicsBuffer> PositionBuffer { get; private set; }
    public ResourceHandle<GraphicsBuffer> InstanceTypeIdBuffer { get; private set; }
    public int Count { get; set; }

    public InstanceRendererData(ResourceHandle<GraphicsBuffer> positionBuffer, ResourceHandle<GraphicsBuffer> instanceTypeIdBuffer, GameObject[] gameObjects, int count)
    {
        GameObjects = gameObjects;
        PositionBuffer = positionBuffer;
        InstanceTypeIdBuffer = instanceTypeIdBuffer;
        Count = count;
    }

    public void Clear(RenderGraph renderGraph)
    {
        renderGraph.BufferHandleSystem.ReleasePersistentResource(PositionBuffer);
        renderGraph.BufferHandleSystem.ReleasePersistentResource(InstanceTypeIdBuffer);
    }
}