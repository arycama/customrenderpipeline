using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

public class BufferHandleSystem : IDisposable
{
    const int swapChainCount = 3;

    private readonly List<BufferHandle> bufferHandlesToCreate = new();
    private readonly Dictionary<GraphicsBuffer, BufferHandle> importedBuffers = new();
    private readonly List<(BufferHandle handle, int lastFrameUsed)> availableBufferHandles = new();
    private readonly List<BufferHandle> usedBufferHandles = new();
    private bool disposedValue;

    public void CleanupCurrentFrame(int frameIndex)
    {
        // Any handles that were not used this frame can be removed
        bufferHandlesToCreate.Clear();
        foreach (var bufferHandle in availableBufferHandles)
        {
            // Keep buffers available for at least two frames
            if (bufferHandle.lastFrameUsed + (swapChainCount - 1) < frameIndex)
                bufferHandle.handle.Dispose();
        }

        availableBufferHandles.Clear();

        foreach (var handle in usedBufferHandles)
            availableBufferHandles.Add((handle, frameIndex));
        usedBufferHandles.Clear();
    }

    public BufferHandle ImportBuffer(GraphicsBuffer buffer)
    {
        return importedBuffers.GetOrAdd(buffer, () => new BufferHandle(buffer));
    }

    public void ReleaseImportedBuffer(GraphicsBuffer buffer)
    {
        var wasRemoved = importedBuffers.Remove(buffer);
        Assert.IsTrue(wasRemoved, "Trying to release a non-imported buffer");
    }

    public void CreateBuffers()
    {
        foreach (var bufferHandle in bufferHandlesToCreate)
            bufferHandle.Create();
    }

    public BufferHandle GetBuffer(int frameIndex, int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
    {
        Assert.IsTrue(count > 0);
        Assert.IsTrue(stride > 0);

        // Find first matching buffer (TODO: Allow returning buffer smaller than required)
        for (var i = 0; i < availableBufferHandles.Count; i++)
        {
            var handle = availableBufferHandles[i];

            // If this buffer can be written to directly, it must have been unused for at least two frames, otherwise it will write to a temp buffer and results
            // will not be visible until the next frame.
            if (usageFlags == GraphicsBuffer.UsageFlags.LockBufferForWrite && handle.lastFrameUsed + (swapChainCount - 1) >= frameIndex)
                continue;

            if (handle.handle.Target != target)
                continue;

            if (handle.handle.Stride != stride)
                continue;

            if (handle.handle.UsageFlags != usageFlags)
                continue;

            if (handle.handle.Target.HasFlag(GraphicsBuffer.Target.Constant))
            {
                // Constant buffers must have exact size
                if (handle.handle.Count != count)
                    continue;
            }
            else if (handle.handle.Count < count)
                continue;

            handle.handle.Size = count * stride;
            availableBufferHandles.RemoveAt(i);
            usedBufferHandles.Add(handle.handle);
            return handle.handle;
        }

        // If no handle was found, create a new one, and assign it as one to be created. 
        var result = new BufferHandle(target, count, stride, usageFlags)
        {
            Size = count * stride
        };
        bufferHandlesToCreate.Add(result);
        usedBufferHandles.Add(result);
        return result;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue)
        {
            Debug.LogError("Disposing an already disposed BufferHandleSystem");
            return;
        }

        if (!disposing)
            Debug.LogError("BufferHandleSystem not disposed correctly");

        foreach (var bufferHandle in availableBufferHandles)
            bufferHandle.handle.Dispose();

        foreach (var handle in importedBuffers)
        {
            if (handle.Value != null)
                handle.Value.Dispose();
        }

        disposedValue = true;
    }

    ~BufferHandleSystem()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}