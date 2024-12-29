using UnityEngine;

public readonly struct BufferHandleDescriptor
{
    public int Count { get; }
    public int Stride { get; }
    public GraphicsBuffer.Target Target { get; }
    public GraphicsBuffer.UsageFlags UsageFlags { get; }

    public BufferHandleDescriptor(int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
    {
        Count = count;
        Stride = stride;
        Target = target;
        UsageFlags = usageFlags;
    }
}
