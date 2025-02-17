﻿using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class ResourceHandleSystem
{
}

public abstract class ResourceHandleSystem<T, V> : ResourceHandleSystem, IDisposable where T : class where V : IResourceDescriptor<T>
{
    private readonly FreeList<(int createIndex, int freeIndex, int resourceIndex, V descriptor, bool isAssigned, bool isReleasable, bool isPersistent, bool isUsed)> handleInfo = new();
    private readonly FreeList<(T resource, int lastFrameUsed, bool isAvailable)> resources = new();
    private readonly Dictionary<T, ResourceHandle<T>> importedResourceLookup = new();
    private readonly List<int> handlesToFree = new();
    private bool disposedValue;

    ~ResourceHandleSystem()
    {
        Dispose(false);
    }

    public ResourceHandle<T> GetResourceHandle(V descriptor, bool isPersistent = false)
    {
        var handleIndex = handleInfo.Add((-1, -1, -1, descriptor, false, false, isPersistent, true));
        return new ResourceHandle<T>(handleIndex);
    }

    public ResourceHandle<T> ImportResource(T resource)
    {
        if (importedResourceLookup.TryGetValue(resource, out var result))
            return result;

        var resourceIndex = resources.Add((resource, -1, false));
        var descriptor = CreateDescriptorFromResource(resource);
        var handleIndex = handleInfo.Add((-1, -1, resourceIndex, descriptor, false, false, true, true));
        result = new ResourceHandle<T>(handleIndex);
        importedResourceLookup.Add(resource, result);

        return result;
    }

    public void WriteResource(ResourceHandle<T> handle, int passIndex)
    {
        // Persistent handles that have already been created don't need to write a create-index
        var info = handleInfo[handle.Index];
        if (info.isPersistent && info.isAssigned)
            return;

        // Initialize or update the index
        info.createIndex = info.createIndex == -1 ? passIndex : Math.Min(passIndex, info.createIndex);
        handleInfo[handle.Index] = info;
    }

    public void ReadResource(ResourceHandle<T> handle, int passIndex)
    {
        var info = handleInfo[handle.Index];
        info.freeIndex = info.freeIndex == -1 ? passIndex : Math.Max(info.freeIndex, passIndex);
        handleInfo[handle.Index] = info;
    }

    public T GetResource(ResourceHandle<T> handle)
    {
        var resourceIndex = handleInfo[handle.Index].resourceIndex;
        return resources[resourceIndex].resource;
    }

    public void ReleasePersistentResource(ResourceHandle<T> handle)
    {
        var info = handleInfo[handle.Index];
        info.isReleasable = true;
        handleInfo[handle.Index] = info;
    }

    public V GetDescriptor(ResourceHandle<T> handle)
    {
        return handleInfo[handle.Index].descriptor;
    }

    public void SetDescriptor(ResourceHandle<T> handle, V descriptor)
    {
        var info = handleInfo[handle.Index];
        info.descriptor = descriptor;
        handleInfo[handle.Index] = info;
    }

    public void AllocateFrameResources(int renderPassCount, int frameIndex)
    {
        List<List<int>> handlesToCreate = new();
        List<List<int>> handlesToFree = new();

        for (var i = 0; i < renderPassCount; i++)
        {
            handlesToCreate.Add(new());
            handlesToFree.Add(new());
        }

        // Add handles to create/free lists if needed
        for (var i = 0; i < handleInfo.Count; i++)
        {
            var (createIndex, freeIndex, _, _, _, isReleasable, isPersistent, isUsed) = handleInfo[i];

            if (!isUsed)
                continue;

            if (createIndex != -1)
                handlesToCreate[createIndex].Add(i);
        
            var freePassIndex = freeIndex;
            if (freePassIndex == -1)
            {
                if (isPersistent && isReleasable)
                    freePassIndex = 0;
                else
                    continue;
            }
            else
            {
                // Do nothing for non-releasable persistent textures
                if (isPersistent && !isReleasable)
                    continue;
            }

            handlesToFree[freePassIndex].Add(i);
        }

        for (var i = 0; i < renderPassCount; i++)
        {
            // Assign or create any RTHandles that are written to by this pass
            foreach (var handle in handlesToCreate[i])
            {
                var info = handleInfo[handle];
                var resourceIndex = -1;
                for (var j = 0; j < resources.Count; j++)
                {
                    var resource = resources[j];

                    if (!resource.isAvailable)
                        continue;

                    if (resource.lastFrameUsed > frameIndex)
                        continue;

                    if (!DoesResourceMatchDescriptor(resource.resource, info.descriptor))
                        continue;

                    resourceIndex = j;

                    // TODO: This should always already be false?
                    resources[resourceIndex] = (resource.resource, resource.lastFrameUsed, false);
                    break;
                }

                if (resourceIndex == -1)
                {
                    var result = info.descriptor.CreateResource(this);
                    resourceIndex = resources.Add((result, -1, false));
                }

                info.isAssigned = true;
                info.createIndex = -1;
                info.freeIndex = -1;
                info.resourceIndex = resourceIndex;
                handleInfo[handle] = info;
            }

            // Now mark any textures that need to be released at the end of this pass as available
            foreach (var handle in handlesToFree[i])
            {
                var (_, _, resourceIndex, descriptor, _, _, isPersistent, _) = handleInfo[handle];

                // Could handle this by updating the last used index or something maybe
                var resource = resources[resourceIndex];
                resources[resourceIndex] = (resource.resource, frameIndex + ExtraFramesToKeepResource(resource.resource), true);

                this.handlesToFree.Add(handle);
            }
        }
    }

    public void CleanupCurrentFrame(int frameIndex)
    {
        // Free handles
        foreach (var handle in handlesToFree)
        {
            handleInfo.Free(handle);
        }

        handlesToFree.Clear();

        // Release any render textures that have not been used for at least a frame
        for (var i = 0; i < resources.Count; i++)
        {
            var resource = resources[i];
            if (!resource.isAvailable)
                continue;

            // Don't free textures that were used in the last frame
            if (resource.lastFrameUsed >= frameIndex)
                continue;

            Debug.LogWarning($"Destroying resource {resources[i]} at index {i}");

            DestroyResource(resource.resource);
            resources.Free(i);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract bool DoesResourceMatchDescriptor(T resource, V descriptor);
    protected abstract void DestroyResource(T resource);
    protected virtual int ExtraFramesToKeepResource(T resource) => 0;
    protected abstract V CreateDescriptorFromResource(T resource);

    private void Dispose(bool disposing)
    {
        if (disposedValue)
        {
            Debug.LogError("Disposing an already disposed ResourceHandleSystem");
            return;
        }

        if (!disposing)
            Debug.LogError("ResourceHandleSystem not disposed correctly");

        for (var i = 0; i < resources.Count; i++)
        {
            var resource = resources[i];
            // Since we don't remove null entries, but rather leave them as "empty", they could be null
            if (resource.resource != null)
            {
                // Persistent resources should be freed first
                //if (!isAvailable[i])
                //Debug.LogError($"Resource at index {i} was not made availalble");

                DestroyResource(resource.resource);
            }
        }

        disposedValue = true;
    }
}
