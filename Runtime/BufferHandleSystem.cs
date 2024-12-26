using System;
using UnityEngine;
using UnityEngine.Assertions;

public class BufferHandleSystem : ResourceHandleSystem<GraphicsBuffer, BufferHandle>
{
    const int swapChainCount = 3;

    public BufferHandle GetResourceHandle(int frameIndex, int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None, bool isPersistent = false)
    {
        Assert.IsTrue(count > 0);
        Assert.IsTrue(stride > 0);

        int index;
        if (isPersistent)
        {
            if (!availablePersistentHandleIndices.TryDequeue(out index))
            {
                index = persistentHandles.Count;
                // TODO: Not sure if I like this. This is because we're adding an index that doesn't currently exist. 
                persistentHandles.Add(null);
                persistentCreateList.Add(-1);
                persistentFreeList.Add(-1);
            }
        }
        else
        {
            index = handles.Count;
        }

        var result = new BufferHandle(target, count, stride, usageFlags, isPersistent);
        result.HandleIndex = index;

        if (isPersistent)
        {
            persistentHandles[index] = result;
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
        return new BufferHandle(resource);
    }

    protected override bool DoesResourceMatchHandle(GraphicsBuffer resource, BufferHandle handle, int frameIndex, int lastFrameUsed)
    {
        // If this buffer can be written to directly, it must have been unused for at least two frames, otherwise it will write to a temp buffer and results
        // will not be visible until the next frame.
        //if (handle.UsageFlags == GraphicsBuffer.UsageFlags.LockBufferForWrite && lastFrameUsed + (swapChainCount - 1) >= frameIndex)
        //    return false;

        if (handle.Target != resource.target)
            return false;

        if (handle.Stride != resource.stride)
            return false;

        if (handle.UsageFlags != resource.usageFlags)
            return false;

        if(handle.Target.HasFlag(GraphicsBuffer.Target.CopySource) || handle.Target.HasFlag(GraphicsBuffer.Target.CopyDestination) || handle.Target.HasFlag(GraphicsBuffer.Target.Constant))
        {
            // Copy source/dest sizes must be exact matches
            if (handle.Count != resource.count)
                return false;

        }
        else if (handle.Count >= resource.count)
        {
            // Other buffers can use smaller sizes than what is actually available
            return false;
        }

        return true;
    }

    protected override GraphicsBuffer CreateResource(BufferHandle handle)
    {
        return new GraphicsBuffer(handle.Target, handle.UsageFlags, handle.Count, handle.Stride)
        {
            name = $"{handle.Target} {handle.UsageFlags} {handle.Stride} {handle.Count} {resourceCount++}"
        };
    }
}