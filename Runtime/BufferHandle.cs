using System;
using UnityEngine;
using UnityEngine.Assertions;

public class BufferHandle : ResourceHandle<GraphicsBuffer>
{
    public GraphicsBuffer.Target Target { get; }
    public int Count { get; }
    public int Stride { get; }
    public GraphicsBuffer.UsageFlags UsageFlags { get; }

    public int Size => Stride * Count;
    
    public BufferHandle(GraphicsBuffer.Target target, int count, int stride, GraphicsBuffer.UsageFlags usageFlags, bool isPersistent)
    {
        Assert.IsTrue(count > 0);
        Assert.IsTrue(stride > 0);

        Target = target;
        Count = count;
        Stride = stride;
        UsageFlags = usageFlags;
        Resource = null;
        IsPersistent = isPersistent;
        IsNotReleasable = isPersistent;
        IsCreated = false;
        IsImported = false;
    }

    public BufferHandle(GraphicsBuffer graphicsBuffer)
    {
        Target = graphicsBuffer.target;
        Count = graphicsBuffer.count;
        Stride = graphicsBuffer.stride;
        Resource = graphicsBuffer;
        IsCreated = true;
        IsImported = true;
    }

    public static implicit operator GraphicsBuffer(BufferHandle bufferHandle)
    {
        return bufferHandle.Resource;
    }

    public override string ToString()
    {
        return $"{Target} {UsageFlags} {Stride}x{Count}";
    }
}
