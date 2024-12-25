using Arycama.CustomRenderPipeline;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

public class BufferHandleSystem : IDisposable
{
    const int swapChainCount = 3;

    private readonly Dictionary<GraphicsBuffer, BufferHandle> importedTextures = new();

    private readonly List<(GraphicsBuffer renderTexture, int lastFrameUsed, bool isAvailable, bool isPersistent)> renderTextures = new();
    private readonly Queue<int> availableRtSlots = new();

    private bool disposedValue;
    private int rtCount;

    private readonly List<BufferHandle> rtHandles = new();
    private readonly List<int> createList = new(), freeList = new();

    private readonly List<BufferHandle> persistentRtHandles = new();
    private readonly Queue<int> availablePersistentHandleIndices = new();
    private readonly List<int> persistentCreateList = new(), persistentFreeList = new();

    ~BufferHandleSystem()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue)
        {
            Debug.LogError("Disposing an already disposed RTHandleSystem");
            return;
        }

        if (!disposing)
            Debug.LogError("RT Handle System not disposed correctly");

        foreach (var rt in renderTextures)
        {
            // Since we don't remove null entries, but rather leave them as "empty", they could be null
            // Also because of the above thing destroying imported textures.. which doesn't really make as much sense, but eh
            if (rt.renderTexture != null)
                rt.renderTexture.Dispose();
        }

        disposedValue = true;
    }


    public BufferHandle GetBuffer(int frameIndex, int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None, bool isPersistent = false)
    {
        Assert.IsTrue(count > 0);
        Assert.IsTrue(stride > 0);

        int index;
        if (isPersistent)
        {
            if (!availablePersistentHandleIndices.TryDequeue(out index))
            {
                index = persistentRtHandles.Count;
                // TODO: Not sure if I like this. This is because we're adding an index that doesn't currently exist. 
                persistentRtHandles.Add(null);
                persistentCreateList.Add(-1);
                persistentFreeList.Add(-1);
            }
        }
        else
        {
            index = rtHandles.Count;
        }

        var result = new BufferHandle(target, count, stride, usageFlags, isPersistent);
        result.Index = index;

        if (isPersistent)
        {
            persistentRtHandles[index] = result;
        }
        else
        {
            rtHandles.Add(result);
            createList.Add(-1);
            freeList.Add(-1);
        }

        return result;
    }

    public BufferHandle ImportBuffer(GraphicsBuffer buffer)
    {
        if (!importedTextures.TryGetValue(buffer, out var result))
        {
            result = new BufferHandle(buffer);
            importedTextures.Add(buffer, result);
        }

        return result;
    }

    public void WriteBuffer(BufferHandle handle, int passIndex)
    {
        // Imported handles don't need create/free logic
        if (handle.IsImported)
            return;

        // Persistent handles that have already been created don't need to write a create-index
        if (handle.IsPersistent && handle.IsCreated)
            return;

        // Select list based on persistent or non-persistent, and initialize or update the index
        var list = handle.IsPersistent ? persistentCreateList : createList;
        var createIndex = list[handle.Index];
        createIndex = createIndex == -1 ? passIndex : Math.Min(passIndex, createIndex);
        list[handle.Index] = createIndex;
    }

    public void ReadBuffer(BufferHandle handle, int passIndex)
    {
        // Ignore imported textures
        if (handle.IsImported)
            return;

        // Do nothing for non-releasable persistent textures
        if (handle.IsPersistent && handle.IsNotReleasable)
            return;

        var list = handle.IsPersistent ? persistentFreeList : freeList;
        var currentIndex = list[handle.Index];
        currentIndex = currentIndex == -1 ? passIndex : Math.Max(currentIndex, passIndex);
        list[handle.Index] = currentIndex;
    }

    private GraphicsBuffer AssignBuffer(BufferHandle handle, int frameIndex)
    {
        // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
        GraphicsBuffer result = null;
        for (var j = 0; j < renderTextures.Count; j++)
        {
            var (buffer, lastFrameUsed, isAvailable, isPersistent) = renderTextures[j];
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
            renderTextures[j] = (buffer, lastFrameUsed, false, handle.IsNotReleasable);
            handle.BufferIndex = j;
            break;
        }

        if (result == null)
        {
            result = new GraphicsBuffer(handle.Target, handle.UsageFlags, handle.Count, handle.Stride)
            {
                name = $"{handle.Target} {handle.UsageFlags} {handle.Stride} {handle.Count} {rtCount++}"
            };

            // Get a slot for this render texture if possible
            if (!availableRtSlots.TryDequeue(out var slot))
            {
                slot = renderTextures.Count;
                renderTextures.Add((result, frameIndex, false, handle.IsNotReleasable));
            }
            else
            {
                renderTextures[slot] = (result, frameIndex, false, handle.IsNotReleasable);
            }

            handle.BufferIndex = slot;
        }

        // Persistent handle no longer needs to be created or cleared. (Non-persistent create list gets cleared every frame)
        if (handle.IsPersistent)
        {
            handle.IsCreated = true;
            persistentCreateList[handle.Index] = -1;
        }

        return result;
    }


    public void FreeTexture(BufferHandle handle, int frameIndex)
    {
        renderTextures[handle.BufferIndex] = (handle.Buffer, frameIndex, true, false);

        // Hrm
        // If non persistent, no additional logic required since it will be re-created, but persistent needs to free its index
        if (handle.IsPersistent)
        {
            availablePersistentHandleIndices.Enqueue(handle.Index);
            persistentFreeList[handle.Index] = -1; // Set to -1 to indicate this doesn't need to be freed again
        }
    }

    public void AllocateFrameTextures(int renderPassCount, int frameIndex)
    {
        List<List<BufferHandle>> texturesToCreate = new();
        List<List<BufferHandle>> texturesToFree = new();

        for (var i = 0; i < renderPassCount; i++)
        {
            texturesToCreate.Add(new());
            texturesToFree.Add(new());
        }

        // Non-persistent create/free requests
        for (var i = 0; i < createList.Count; i++)
        {
            var passIndex = createList[i];
            if (passIndex != -1)
                texturesToCreate[passIndex].Add(rtHandles[i]);
        }

        for (var i = 0; i < freeList.Count; i++)
        {
            var passIndex = freeList[i];
            if (passIndex != -1)
                texturesToFree[passIndex].Add(rtHandles[i]);
        }

        // Persistent create/free requests
        for (var i = 0; i < persistentCreateList.Count; i++)
        {
            var passIndex = persistentCreateList[i];
            if (passIndex != -1)
                texturesToCreate[passIndex].Add(persistentRtHandles[i]);
        }

        for (var i = 0; i < persistentFreeList.Count; i++)
        {
            var passIndex = persistentFreeList[i];
            if (passIndex != -1)
                texturesToFree[passIndex].Add(persistentRtHandles[i]);
        }

        for (var i = 0; i < renderPassCount; i++)
        {
            // Assign or create any RTHandles that are written to by this pass
            foreach (var handle in texturesToCreate[i])
            {
                handle.Buffer = AssignBuffer(handle, frameIndex);
            }

            // Now mark any textures that need to be released at the end of this pass as available
            foreach (var output in texturesToFree[i])
            {
                FreeTexture(output, frameIndex);
            }
        }
    }

    public void CleanupCurrentFrame(int frameIndex)
    {
        // Release any render textures that have not been used for at least a frame
        for (var i = 0; i < renderTextures.Count; i++)
        {
            var renderTexture = renderTextures[i];

            // This indicates it is empty
            if (renderTexture.renderTexture == null)
                continue;

            if (renderTexture.isPersistent)
                continue;

            // Don't free textures that were used in the last frame
            // TODO: Make this a configurable number of frames to avoid rapid re-allocations
            if (renderTexture.lastFrameUsed + (swapChainCount - 1) >= frameIndex)
                continue;

            renderTexture.renderTexture.Dispose();

            // Fill this with a null, unavailable RT and add the index to a list
            renderTextures[i] = (null, renderTexture.lastFrameUsed, false, false);
            availableRtSlots.Enqueue(i);
        }

        rtHandles.Clear();
        createList.Clear();
        freeList.Clear();
    }
}