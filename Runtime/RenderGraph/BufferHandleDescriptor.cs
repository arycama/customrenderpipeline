using UnityEngine;
using UnityEngine.Assertions;

public readonly struct BufferHandleDescriptor : IResourceDescriptor<GraphicsBuffer>
{
	public int Count { get; }
	public int Stride { get; }
	public GraphicsBuffer.Target Target { get; }
	public GraphicsBuffer.UsageFlags UsageFlags { get; }

	public BufferHandleDescriptor(int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
	{
		Assert.AreNotEqual(count, 0);
		Assert.AreNotEqual(stride, 0);

		Count = count;
		Stride = stride;
		Target = target;
		UsageFlags = usageFlags;
	}

	public GraphicsBuffer CreateResource(ResourceHandleSystemBase system)
	{
		return new GraphicsBuffer(Target, UsageFlags, Count, Stride);
	}
}