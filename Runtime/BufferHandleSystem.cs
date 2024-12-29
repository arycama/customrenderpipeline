using UnityEngine;

public class BufferHandleSystem : ResourceHandleSystem<GraphicsBuffer, BufferHandle, BufferHandleDescriptor>
{
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

    protected override GraphicsBuffer CreateResource(BufferHandleDescriptor descriptor)
    {
        return new GraphicsBuffer(descriptor.Target, descriptor.UsageFlags, descriptor.Count, descriptor.Stride);
    }

    protected override int ExtraFramesToKeepResource(GraphicsBuffer resource)
    {
        return resource.usageFlags.HasFlag(GraphicsBuffer.UsageFlags.LockBufferForWrite) ? 3 : 0;
    }

    protected override BufferHandle CreateHandleFromDescriptor(BufferHandleDescriptor descriptor, bool isPersistent, int handleIndex)
    {
        return new BufferHandle(handleIndex, isPersistent, descriptor);
    }

    protected override BufferHandleDescriptor CreateDescriptorFromResource(GraphicsBuffer resource)
    {
        return new BufferHandleDescriptor(resource.count, resource.stride, resource.target, resource.usageFlags);
    }
}