using System;
using UnityEngine;
using UnityEngine.Assertions;

public class BufferHandle
{
    public int Size { get; set; }
    public GraphicsBuffer Buffer { get; set; }

    public GraphicsBuffer.Target Target { get; }
    public int Count { get; }
    public int Stride { get; }
    public GraphicsBuffer.UsageFlags UsageFlags { get; }
    public bool IsPersistent { get; }
    public bool IsNotReleasable { get; }
    public bool IsCreated { get; set; }
    public bool IsImported { get; }
    public int Index { get; set; }
    public int BufferIndex { get; set; }

    public BufferHandle(GraphicsBuffer.Target target, int count, int stride, GraphicsBuffer.UsageFlags usageFlags, bool isPersistent)
    {
        Assert.IsTrue(count > 0);
        Assert.IsTrue(stride > 0);

        Target = target;
        Count = count;
        Stride = stride;
        UsageFlags = usageFlags;
        Buffer = null;
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
        Buffer = graphicsBuffer;
        Size = graphicsBuffer.count * graphicsBuffer.stride;
        IsCreated = true;
        IsImported = true;
    }

    public static implicit operator GraphicsBuffer(BufferHandle bufferHandle)
    {
        return bufferHandle.Buffer;
    }
}
