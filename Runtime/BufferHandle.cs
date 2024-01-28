using UnityEngine;
using UnityEngine.Assertions;

public class BufferHandle
{
    public int Size { get; set; }

    public GraphicsBuffer.Target Target { get; }
    public int Count { get; }
    public int Stride { get; }
    private GraphicsBuffer buffer;

    public BufferHandle(GraphicsBuffer.Target target, int count, int stride)
    {
        Assert.IsTrue(count > 0);
        Assert.IsTrue(stride > 0);

        Target = target;
        Count = count;
        Stride = stride;
        buffer = null;
    }

    public void Create()
    {
        Assert.IsNull(buffer);
        buffer = new GraphicsBuffer(Target, Count, Stride)
        {
            name = $"BufferHandle {Target} {Count} {Stride}"
        };
    }

    public void Release()
    {
        buffer.Release();
    }

    public static implicit operator GraphicsBuffer(BufferHandle bufferHandle)
    {
        return bufferHandle.buffer;
    }

    public static implicit operator BufferHandle(GraphicsBuffer graphicsBuffer)
    {
        return new BufferHandle(graphicsBuffer.target, graphicsBuffer.count, graphicsBuffer.stride)
        {
            buffer = graphicsBuffer,
            Size = graphicsBuffer.count * graphicsBuffer.stride
        };
    }
}
