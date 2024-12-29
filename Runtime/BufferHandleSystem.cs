using UnityEngine;

public class BufferHandleSystem : ResourceHandleSystem<GraphicsBuffer, BufferHandle, BufferHandleDescriptor>
{
    protected override void DestroyResource(GraphicsBuffer resource)
    {
        resource.Dispose();
    }

    protected override BufferHandle CreateHandleFromResource(GraphicsBuffer resource, int index)
    {
        return new BufferHandle(resource, index, true, true);
    }

    protected override bool DoesResourceMatchHandle(GraphicsBuffer resource, BufferHandle handle)
    {
        if (handle.Target != resource.target)
            return false;

        if (handle.Stride != resource.stride)
            return false;

        if (handle.UsageFlags != resource.usageFlags)
            return false;

        if (handle.Count != resource.count)
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

    protected override GraphicsBuffer CreateResource(BufferHandle handle)
    {
        return new GraphicsBuffer(handle.Target, handle.UsageFlags, handle.Count, handle.Stride)
        {
            name = $"{handle.Target} {handle.UsageFlags} {handle.Stride} {handle.Count} {resourceCount++}"
        };
    }

    protected override int ExtraFramesToKeepResource(GraphicsBuffer resource)
    {
        return resource.usageFlags.HasFlag(GraphicsBuffer.UsageFlags.LockBufferForWrite) ? 3 : 0;
    }

    protected override BufferHandle CreateHandleFromDescriptor(BufferHandleDescriptor descriptor, bool isPersistent, int handleIndex)
    {
        return new BufferHandle(handleIndex, false, isPersistent, descriptor.Target, descriptor.Count, descriptor.Stride, descriptor.UsageFlags);
    }
}