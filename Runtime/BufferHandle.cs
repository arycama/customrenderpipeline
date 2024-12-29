using UnityEngine;

public class BufferHandle : IResourceHandle
{
    public int HandleIndex { get; }
    public bool IsPersistent { get; }
    public GraphicsBuffer.Target Target { get; }
    public int Count { get; }
    public int Stride { get; }
    public GraphicsBuffer.UsageFlags UsageFlags { get; }

    public int Size => Stride * Count;
    
    public BufferHandle(int handleIndex, bool isPersistent, GraphicsBuffer.Target target, int count, int stride, GraphicsBuffer.UsageFlags usageFlags)
    {
        HandleIndex = handleIndex;
        IsPersistent = isPersistent;

        Target = target;
        Count = count;
        Stride = stride;
        UsageFlags = usageFlags;
    }

    public BufferHandle(GraphicsBuffer graphicsBuffer, int handleIndex, bool isPersistent)
    {

        HandleIndex = handleIndex;
        IsPersistent = isPersistent;

        Target = graphicsBuffer.target;
        Count = graphicsBuffer.count;
        Stride = graphicsBuffer.stride;
    }
}