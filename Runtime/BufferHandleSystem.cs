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

    protected override GraphicsBuffer AssignResource(BufferHandle handle, int frameIndex)
    {
        // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
        int slot = -1;
        GraphicsBuffer result = null;
        for (var j = 0; j < resources.Count; j++)
        {
            var (buffer, lastFrameUsed, isAvailable) = resources[j];
            if (!isAvailable)
                continue;

            // If this buffer can be written to directly, it must have been unused for at least two frames, otherwise it will write to a temp buffer and results
            // will not be visible until the next frame.
            if (handle.UsageFlags == GraphicsBuffer.UsageFlags.LockBufferForWrite && lastFrameUsed + (swapChainCount - 1) >= frameIndex)
                continue;

            if (handle.Target != buffer.target)
                continue;

            if (handle.Stride != buffer.stride)
                continue;

            if (handle.UsageFlags != buffer.usageFlags)
                continue;

            if (handle.Target.HasFlag(GraphicsBuffer.Target.Constant))
            {
                // Constant buffers must have exact size
                if (handle.Count != buffer.count)
                    continue;
            }
            else if (handle.Count >= buffer.count)
                continue;

            result = buffer;
            slot = j;
            break;
        }

        if (result == null)
        {
            result = new GraphicsBuffer(handle.Target, handle.UsageFlags, handle.Count, handle.Stride)
            {
                name = $"{handle.Target} {handle.UsageFlags} {handle.Stride} {handle.Count} {resourceCount++}"
            };

            // Get a slot for this render texture if possible
            if (!availableSlots.TryDequeue(out slot))
            {
                slot = resources.Count;
                resources.Add(default);
            }
        }

        // Persistent handle no longer needs to be created or cleared. (Non-persistent create list gets cleared every frame)
        if (handle.IsPersistent)
        {
            handle.IsCreated = true;
            persistentCreateList[handle.HandleIndex] = -1;
        }

        resources[slot] = (result, frameIndex, false);

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
}