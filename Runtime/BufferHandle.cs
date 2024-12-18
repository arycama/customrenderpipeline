using System;
using UnityEngine;
using UnityEngine.Assertions;

public class BufferHandle : IDisposable
{
    private bool disposedValue;

    public int Size { get; set; }
    public GraphicsBuffer Buffer { get; private set; }

    public GraphicsBuffer.Target Target { get; }
    public int Count { get; }
    public int Stride { get; }
    public GraphicsBuffer.UsageFlags UsageFlags { get; }

    public BufferHandle(GraphicsBuffer.Target target, int count, int stride, GraphicsBuffer.UsageFlags usageFlags)
    {
        Assert.IsTrue(count > 0);
        Assert.IsTrue(stride > 0);

        Target = target;
        Count = count;
        Stride = stride;
        UsageFlags = usageFlags;
        Buffer = null;
    }

    public BufferHandle(GraphicsBuffer graphicsBuffer)
    {
        Target = graphicsBuffer.target;
        Count = graphicsBuffer.count;
        Stride = graphicsBuffer.stride;
        Buffer = graphicsBuffer;
        Size = graphicsBuffer.count * graphicsBuffer.stride;
    }

    public void Create()
    {
        Assert.IsNull(Buffer);
        Buffer = new GraphicsBuffer(Target, UsageFlags, Count, Stride)
        {
            name = $"BufferHandle {Target} {Count} {Stride}"
        };
    }

    public static implicit operator GraphicsBuffer(BufferHandle bufferHandle)
    {
        return bufferHandle.Buffer;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue)
            return;

        if (Buffer != null)
        {
            Buffer.Release();
            Buffer = null;
        }

        if(!disposing)
            Debug.LogError("Buffer Handle not disposed correctly");

        disposedValue = true;
    }

    ~BufferHandle()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
