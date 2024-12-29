﻿using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class ResourceHandleSystem<T, K> : IDisposable where T : class where K : ResourceHandle<T>
{
    protected readonly List<K> handles = new();
    protected readonly List<int> createList = new(), freeList = new();

    protected readonly List<K> persistentHandles = new();
    protected readonly Queue<int> availablePersistentHandleIndices = new();
    protected readonly List<int> persistentCreateList = new(), persistentFreeList = new();
    protected readonly List<int> resourceIndices = new(), persistentResourceIndices = new();

    protected int resourceCount;

    private readonly Dictionary<T, K> importedResources = new();
    private readonly List<T> resources = new();
    private readonly List<int> lastFrameUsed = new();
    private readonly List<bool> isAvailable = new();
    private readonly Queue<int> availableSlots = new();
    private bool disposedValue;

    ~ResourceHandleSystem()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

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
            if (resource != null)
            {
                // Persistent resources should be freed first
                if (!isAvailable[i])
                    Debug.LogError($"Resource at index {i} was not made availalble");

                DestroyResource(resource);
            }
        }

        disposedValue = true;
    }

    protected abstract bool DoesResourceMatchHandle(T resource, K handle);
    protected abstract T CreateResource(K handle);
    protected abstract K CreateHandleFromResource(T resource);
    protected abstract void DestroyResource(T resource);
    protected virtual int ExtraFramesToKeepResource(T resource) => 0;

    private T AssignResource(K handle, int frameIndex)
    {
        // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
        int slot = -1;
        T result = null;
        for (var j = 0; j < resources.Count; j++)
        {
            if (!isAvailable[j])
                continue;

            var resource = resources[j];
            if (!DoesResourceMatchHandle(resource, handle))
                continue;

            result = resource;
            slot = j;
            break;
        }

        if (result == null)
        {
            result = CreateResource(handle);

            // Get a slot for this render texture if possible
            if (!availableSlots.TryDequeue(out slot))
            {
                slot = resources.Count;
                resources.Add(result);
                lastFrameUsed.Add(frameIndex + ExtraFramesToKeepResource(result));
                isAvailable.Add(false);
            }
            else
            {
                resources[slot] = result;
                lastFrameUsed[slot] = frameIndex + ExtraFramesToKeepResource(result);
                isAvailable[slot] = false; // Already false 
            }
        }
        else
        {
            lastFrameUsed[slot] = frameIndex + ExtraFramesToKeepResource(result);
            isAvailable[slot] = false;
        }

        // Persistent handle no longer needs to be created or cleared. (Non-persistent create list gets cleared every frame)
        if (handle.IsPersistent)
        {
            handle.IsAssigned = true;
            persistentCreateList[handle.HandleIndex] = -1;
            persistentResourceIndices[handle.HandleIndex] = slot;
        }
        else
        {
            resourceIndices[handle.HandleIndex] = slot;
        }

        return result;
    }


    public K ImportResource(T resource)
    {
        if (!importedResources.TryGetValue(resource, out var result))
        {
            result = CreateHandleFromResource(resource);
            importedResources.Add(resource, result);
        }

        return result;
    }

    public void WriteResource(K handle, int passIndex)
    {
        // Imported handles don't need create/free logic
        if (handle.IsImported)
            return;

        // Persistent handles that have already been created don't need to write a create-index
        if (handle.IsPersistent && handle.IsAssigned)
            return;

        // Select list based on persistent or non-persistent, and initialize or update the index
        var list = handle.IsPersistent ? persistentCreateList : createList;
        var createIndex = list[handle.HandleIndex];
        createIndex = createIndex == -1 ? passIndex : Math.Min(passIndex, createIndex);
        list[handle.HandleIndex] = createIndex;
    }

    public void ReadResource(K handle, int passIndex)
    {
        // Ignore imported textures
        if (handle.IsImported)
            return;

        // Do nothing for non-releasable persistent textures
        if (handle.IsPersistent && handle.IsNotReleasable)
            return;

        var list = handle.IsPersistent ? persistentFreeList : freeList;
        var currentIndex = list[handle.HandleIndex];
        currentIndex = currentIndex == -1 ? passIndex : Math.Max(currentIndex, passIndex);
        list[handle.HandleIndex] = currentIndex;
    }

    public void AllocateFrameResources(int renderPassCount, int frameIndex)
    {
        List<List<K>> handlesToCreate = new();
        List<List<K>> handlesToFree = new();

        for (var i = 0; i < renderPassCount; i++)
        {
            handlesToCreate.Add(new());
            handlesToFree.Add(new());
        }

        // Non-persistent create/free requests
        for (var i = 0; i < createList.Count; i++)
        {
            var passIndex = createList[i];
            if (passIndex != -1)
                handlesToCreate[passIndex].Add(handles[i]);
        }

        for (var i = 0; i < freeList.Count; i++)
        {
            var passIndex = freeList[i];
            if (passIndex != -1)
                handlesToFree[passIndex].Add(handles[i]);
        }

        // Persistent create/free requests
        for (var i = 0; i < persistentCreateList.Count; i++)
        {
            var passIndex = persistentCreateList[i];
            if (passIndex != -1)
                handlesToCreate[passIndex].Add(persistentHandles[i]);
        }

        for (var i = 0; i < persistentFreeList.Count; i++)
        {
            var passIndex = persistentFreeList[i];
            if (passIndex != -1)
                handlesToFree[passIndex].Add(persistentHandles[i]);
        }

        for (var i = 0; i < renderPassCount; i++)
        {
            // Assign or create any RTHandles that are written to by this pass
            foreach (var handle in handlesToCreate[i])
            {
                handle.Resource = AssignResource(handle, frameIndex);
            }

            // Now mark any textures that need to be released at the end of this pass as available
            foreach (var handle in handlesToFree[i])
            {
                // Todo: too much indirection?
                var resourceIndex = handle.IsPersistent ? persistentResourceIndices[handle.HandleIndex] : resourceIndices[handle.HandleIndex];
                isAvailable[resourceIndex] = true;

                // If non persistent, no additional logic required since it will be re-created, but persistent needs to free its index
                if (handle.IsPersistent)
                {
                    availablePersistentHandleIndices.Enqueue(handle.HandleIndex);
                    persistentFreeList[handle.HandleIndex] = -1; // Set to -1 to indicate this doesn't need to be freed again
                }
            }
        }
    }

    public void CleanupCurrentFrame(int frameIndex)
    {
        // Release any render textures that have not been used for at least a frame
        for (var i = 0; i < resources.Count; i++)
        {
            if (!isAvailable[i])
                continue;

            // Don't free textures that were used in the last frame
            // TODO: Make this a configurable number of frames to avoid rapid re-allocations
            if (lastFrameUsed[i] >= frameIndex)
                continue;

            DestroyResource(resources[i]);

            Debug.LogWarning($"Destroying resource at index {i}");

            isAvailable[i] = false;
            availableSlots.Enqueue(i);
        }

        handles.Clear();
        resourceIndices.Clear();
        createList.Clear();
        freeList.Clear();
    }
}
