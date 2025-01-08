using System;
using System.Collections.Generic;
using UnityEngine;

public class FreeList<T>
{
    private readonly Stack<int> availableIndices = new();
    private readonly List<T> items = new();

    public int Count => items.Count;

    public T this[int i]
    {
        get => items[i];
        set => items[i] = value;
    }

    public int Add(T item)
    {
        if (availableIndices.TryPop(out var index))
        {
            items[index] = item;
        }
        else
        {
            index = items.Count;
            items.Add(item);
        }

        return index;
    }

    public void Free(int index)
    {
        items[index] = default;
        availableIndices.Push(index);
    }
}

public abstract class ResourceHandleSystem<T, V> : IDisposable where T : class where V : IResourceDescriptor<T>
{
    private int handleCount;
    private readonly Stack<int> availableHandleIndices = new();
    private readonly List<int> createList = new(), freeList = new();
    private readonly List<int> resourceIndices = new();
    private readonly List<V> descriptors = new();
    private readonly List<bool> isAssigned = new();
    private readonly List<bool> isReleasable = new();
    private readonly List<bool> isPersistent = new();

    private readonly Dictionary<T, ResourceHandle<T>> importedResourceLookup = new();

    // Per resource
    private readonly FreeList<(T resource, int lastFrameUsed, bool isAvailable)> resources = new();

    private bool disposedValue;

    ~ResourceHandleSystem()
    {
        Dispose(false);
    }

    public ResourceHandle<T> GetResourceHandle(V descriptor, bool isPersistent = false)
    {
        ResourceHandle<T> result;
        if (availableHandleIndices.TryPop(out var handleIndex))
        {
            result = new ResourceHandle<T>(handleIndex);
            this.isPersistent[handleIndex] = isPersistent;

            isAssigned[handleIndex] = false;
            isReleasable[handleIndex] = false;
            descriptors[handleIndex] = descriptor;

            resourceIndices[handleIndex] = -1;
            createList[handleIndex] = -1;
            freeList[handleIndex] = -1;
        }
        else
        {
            handleIndex = handleCount++;
            result = new ResourceHandle<T>(handleIndex);
            this.isPersistent.Add(isPersistent);

            isAssigned.Add(false);
            isReleasable.Add(false);
            descriptors.Add(descriptor);

            resourceIndices.Add(-1);
            createList.Add(-1);
            freeList.Add(-1);
        }

        return result;
    }

    public ResourceHandle<T> ImportResource(T resource)
    {
        if (!importedResourceLookup.TryGetValue(resource, out var result))
        {
            var descriptor = CreateDescriptorFromResource(resource);

            var resourceIndex = resources.Count;
            resources.Add((resource, -1, false));

            if (availableHandleIndices.TryPop(out var handleIndex))
            {
                result = new ResourceHandle<T>(handleIndex);
                isPersistent[handleIndex] = true;

                isAssigned[handleIndex] = false;
                isReleasable[handleIndex] = false;
                descriptors[handleIndex] = descriptor;

                resourceIndices[handleIndex] = resourceIndex;
                createList[handleIndex] = -1;
                freeList[handleIndex] = -1;
            }
            else
            {
                handleIndex = handleCount++;
                result = new ResourceHandle<T>(handleIndex);
                isPersistent.Add(true);

                isAssigned.Add(false);
                isReleasable.Add(false);
                descriptors.Add(descriptor);

                resourceIndices.Add(resourceIndex);
                createList.Add(-1);
                freeList.Add(-1);
            }

            importedResourceLookup.Add(resource, result);
        }

        return result;
    }


    public void WriteResource(ResourceHandle<T> handle, int passIndex)
    {
        // Persistent handles that have already been created don't need to write a create-index
        if (isPersistent[handle.Index] && isAssigned[handle.Index])
            return;

        // Select list based on persistent or non-persistent, and initialize or update the index
        var createIndex = createList[handle.Index];
        createIndex = createIndex == -1 ? passIndex : Math.Min(passIndex, createIndex);
        createList[handle.Index] = createIndex;
    }

    public void ReadResource(ResourceHandle<T> handle, int passIndex)
    {
        var currentIndex = freeList[handle.Index];
        currentIndex = currentIndex == -1 ? passIndex : Math.Max(currentIndex, passIndex);
        freeList[handle.Index] = currentIndex;
    }

    public T GetResource(ResourceHandle<T> handle)
    {
        var resourceIndex = resourceIndices[handle.Index];
        return resources[resourceIndex].resource;
    }

    public void ReleasePersistentResource(ResourceHandle<T> handle)
    {
        // TODO: This can trigger from gpu instaned rendere data which can be readback while cleanup is occuring, resulting in two releases.
        //Assert.IsFalse(isReleasable[handle.Index], "Trying to release a non persistent resource");
        isReleasable[handle.Index] = true;
    }

    public V GetDescriptor(ResourceHandle<T> handle)
    {
        return descriptors[handle.Index];
    }

    public void SetDescriptor(ResourceHandle<T> handle, V descriptor)
    {
        descriptors[handle.Index] = descriptor;
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

        // Non-persistent create/free requests
        for (var i = 0; i < createList.Count; i++)
        {
            var passIndex = createList[i];
            if (passIndex != -1)
                handlesToCreate[passIndex].Add(i);
        }

        for (var i = 0; i < freeList.Count; i++)
        {
            var passIndex = freeList[i];
            if (passIndex == -1)
            {
                if (isPersistent[i] && isReleasable[i])
                    passIndex = 0;
                else
                    continue;
            }
            else
            {
                // Do nothing for non-releasable persistent textures
                if (isPersistent[i] && !isReleasable[i])
                    continue;
            }

            handlesToFree[passIndex].Add(i);
        }

        for (var i = 0; i < renderPassCount; i++)
        {
            // Assign or create any RTHandles that are written to by this pass
            foreach (var handle in handlesToCreate[i])
            {
                var descriptor = descriptors[handle];
                var resourceIndex = -1;
                for (var j = 0; j < resources.Count; j++)
                {
                    var resource = resources[j];

                    if (!resource.isAvailable)
                        continue;

                    if (resource.lastFrameUsed > frameIndex)
                        continue;

                    if (!DoesResourceMatchDescriptor(resource.resource, descriptor))
                        continue;

                    resourceIndex = j;

                    // TODO: This should always already be false?
                    resources[resourceIndex] = (resource.resource, resource.lastFrameUsed, false);
                    break;
                }

                if (resourceIndex == -1)
                {
                    var result = descriptor.CreateResource();
                    resourceIndex = resources.Add((result, -1, false));
                }

                isAssigned[handle] = true;
                createList[handle] = -1;
                freeList[handle] = -1;
                resourceIndices[handle] = resourceIndex;
            }

            // Now mark any textures that need to be released at the end of this pass as available
            foreach (var handle in handlesToFree[i])
            {
                // Free some handle-related variables
                isPersistent[handle] = false;
                isReleasable[handle] = false;
                isAssigned[handle] = false;
                createList[handle] = -1;
                freeList[handle] = -1;

                // Todo: too much indirection?
                var resourceIndex = resourceIndices[handle];
                var resource = resources[resourceIndex];
                resources[resourceIndex] = (resource.resource, frameIndex, true);
                availableHandleIndices.Push(handle);
            }
        }
    }

    public void CleanupCurrentFrame(int frameIndex)
    {
        // Release any render textures that have not been used for at least a frame
        for (var i = 0; i < resources.Count; i++)
        {
            var resource = resources[i];
            if (!resource.isAvailable)
                continue;

            // Don't free textures that were used in the last frame
            if (resource.lastFrameUsed + ExtraFramesToKeepResource(resource.resource) >= frameIndex)
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
