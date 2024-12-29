using UnityEngine;

public class BufferHandleSystem : ResourceHandleSystem<GraphicsBuffer, BufferHandleDescriptor>
{
    protected override GraphicsBuffer CreateResource(BufferHandleDescriptor descriptor) => new(descriptor.Target, descriptor.UsageFlags, descriptor.Count, descriptor.Stride);

    protected override int ExtraFramesToKeepResource(GraphicsBuffer resource) => resource.usageFlags.HasFlag(GraphicsBuffer.UsageFlags.LockBufferForWrite) ? 3 : 0;

    protected override ResourceHandle<GraphicsBuffer> CreateHandle(int handleIndex, bool isPersistent) => new(handleIndex, isPersistent);

    protected override BufferHandleDescriptor CreateDescriptorFromResource(GraphicsBuffer resource) => new(resource.count, resource.stride, resource.target, resource.usageFlags);

    protected override void DestroyResource(GraphicsBuffer resource)
    {
        resource.Dispose();
    }

    protected override bool DoesResourceMatchDescriptor(GraphicsBuffer resource, BufferHandleDescriptor descriptor)
    {
        if (descriptor.Target != resource.target)
            return false;

        if (descriptor.Stride != resource.stride)
            return false;

        if (descriptor.UsageFlags != resource.usageFlags)
            return false;

        if (descriptor.Count != resource.count)
            return false;

        //if (handle.Target.HasFlag(GraphicsBuffer.Target.CopySource) || handle.Target.HasFlag(GraphicsBuffer.Target.CopyDestination) || handle.Target.HasFlag(GraphicsBuffer.Target.Constant))
        //{
        //    // Copy source/dest sizes must be exact matches
        //    if (handle.Count != resource.count)
        //        return false;

        //}
        //else if (handle.Count >= resource.count)
        //{
        //    // Other buffers can use smaller sizes than what is actually available
        //    return false;
        //}

        return true;
    }
}