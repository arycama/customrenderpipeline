using UnityEngine;
using UnityEngine.Assertions;

public class BufferHandle : ResourceHandle<GraphicsBuffer>
{
    public GraphicsBuffer.Target Target { get; }
    public int Count { get; }
    public int Stride { get; }
    public GraphicsBuffer.UsageFlags UsageFlags { get; }

    public int Size => Stride * Count;
    
    public BufferHandle(int handleIndex, bool isPersistent, GraphicsBuffer.Target target, int count, int stride, GraphicsBuffer.UsageFlags usageFlags) : base(handleIndex, isPersistent)
    {
        Assert.IsTrue(count > 0);
        Assert.IsTrue(stride > 0);

        Target = target;
        Count = count;
        Stride = stride;
        UsageFlags = usageFlags;
    }

    public BufferHandle(GraphicsBuffer graphicsBuffer, int handleIndex, bool isPersistent) : base(handleIndex, isPersistent)
    {
        Target = graphicsBuffer.target;
        Count = graphicsBuffer.count;
        Stride = graphicsBuffer.stride;
    }

    public override string ToString()
    {
        return $"{Target} {UsageFlags} {Stride}x{Count}";
    }
}
