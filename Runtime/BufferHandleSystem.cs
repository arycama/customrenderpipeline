using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

public class BufferHandleSystem : ResourceHandleSystem<GraphicsBuffer, BufferHandle>
{
    const int swapChainCount = 3;

    public BufferHandle GetResourceHandle(int frameIndex, int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None, bool isPersistent = false)
    {
        int handleIndex;
        if (isPersistent)
        {
            if (!availablePersistentHandleIndices.TryDequeue(out handleIndex))
            {
                handleIndex = persistentHandles.Count;
                persistentHandles.Add(null);
                persistentCreateList.Add(-1);
                persistentFreeList.Add(-1);
            }
        }
        else
        {
            handleIndex = handles.Count;
        }

        var result = new BufferHandle(handleIndex, false, isPersistent, target, count, stride, usageFlags);

        if (isPersistent)
        {
            persistentHandles[handleIndex] = result;
        }
        else
        {
            handles.Add(result);
            createList.Add(-1);
            freeList.Add(-1);
        }

        return result;
    }

    protected override void DestroyResource(GraphicsBuffer resource)
    {
        resource.Dispose();
    }

    protected override BufferHandle CreateHandleFromResource(GraphicsBuffer resource)
    {
        return new BufferHandle(resource, -1, true, true);
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
}