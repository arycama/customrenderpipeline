using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class ResourceHandleSystem<T, V> : IDisposable where T : class where V : IResourceDescriptor<T>
{
    private readonly List<ResourceHandle<T>> handles = new();
    private readonly List<int> createList = new(), freeList = new();
    private readonly List<V> descriptors = new();

    private readonly List<ResourceHandle<T>> persistentHandles = new();
    private readonly Stack<int> availablePersistentHandleIndices = new();
    private readonly List<int> persistentCreateList = new(), persistentFreeList = new();
    private readonly List<int> resourceIndices = new(), persistentResourceIndices = new();
    private readonly List<V> persistentDescriptors = new();

    private readonly Dictionary<T, ResourceHandle<T>> importedResourceLookup = new();
    private readonly List<T> importedResources = new();
    private readonly List<V> importedDescriptors = new();

    private readonly List<T> resources = new();
    private readonly List<int> lastFrameUsed = new();
    private readonly List<bool> isAvailable = new();
    private readonly List<bool> isAssigned = new();
    private readonly List<bool> isReleasable = new();
    private readonly Stack<int> availableSlots = new();

    private bool disposedValue;

    ~ResourceHandleSystem()
    {
        Dispose(false);
    }
    public ResourceHandle<T> ImportResource(T resource)
    {
        if (!importedResourceLookup.TryGetValue(resource, out var result))
        {
            var index = importedResources.Count;
            importedResources.Add(resource);

            var descriptor = CreateDescriptorFromResource(resource);
            importedDescriptors.Add(descriptor);

            result = new ResourceHandle<T>(-index, true);
            importedResourceLookup.Add(resource, result);
        }

        return result;
    }

    public ResourceHandle<T> GetResourceHandle(V descriptor, bool isPersistent = false)
    {
        ResourceHandle<T> result;
        if (isPersistent)
        {
            if (availablePersistentHandleIndices.TryPop(out var handleIndex))
            {
                result = new ResourceHandle<T>(handleIndex, isPersistent);
                persistentHandles[handleIndex] = result;

                isAssigned[handleIndex] = false;
                isReleasable[handleIndex] = false;
                persistentDescriptors[handleIndex] = descriptor;

                persistentResourceIndices[handleIndex] = -1;
                persistentCreateList[handleIndex] = -1;
                persistentFreeList[handleIndex] = -1;
            }
            else
            {
                handleIndex = persistentHandles.Count;
                result = new ResourceHandle<T>(handleIndex, isPersistent);
                persistentHandles.Add(result);

                isAssigned.Add(false);
                isReleasable.Add(false);
                persistentDescriptors.Add(descriptor);

                persistentResourceIndices.Add(-1);
                persistentCreateList.Add(-1);
                persistentFreeList.Add(-1);
            }
        }
        else
        {
            result = new ResourceHandle<T>(handles.Count, isPersistent);
            handles.Add(result);
            descriptors.Add(descriptor);

            resourceIndices.Add(-1);
            createList.Add(-1);
            freeList.Add(-1);
        }

        return result;
    }

    public void WriteResource(ResourceHandle<T> handle, int passIndex)
    {
        // Imported handles don't need create/free logic
        if (handle.Index < 0) // Negative 
            return;

        // Persistent handles that have already been created don't need to write a create-index
        if (handle.IsPersistent && isAssigned[handle.Index])
            return;

        // Select list based on persistent or non-persistent, and initialize or update the index
        var list = handle.IsPersistent ? persistentCreateList : createList;
        var createIndex = list[handle.Index];
        createIndex = createIndex == -1 ? passIndex : Math.Min(passIndex, createIndex);
        list[handle.Index] = createIndex;
    }

    public void ReadResource(ResourceHandle<T> handle, int passIndex)
    {
        // Ignore imported textures
        if (handle.Index < 0)
            return;

        // Do nothing for non-releasable persistent textures
        if (handle.IsPersistent && !isReleasable[handle.Index])
            return;

        var list = handle.IsPersistent ? persistentFreeList : freeList;
        var currentIndex = list[handle.Index];
        currentIndex = currentIndex == -1 ? passIndex : Math.Max(currentIndex, passIndex);
        list[handle.Index] = currentIndex;
    }

    public T GetResource(ResourceHandle<T> handle)
    {
        if (handle.Index < 0)
            return importedResources[-handle.Index];

        var indexList = handle.IsPersistent ? persistentResourceIndices : resourceIndices;
        var resourceIndex = indexList[handle.Index];
        return resources[resourceIndex];
    }

    public void ReleasePersistentResource(ResourceHandle<T> handle)
    {
        isReleasable[handle.Index] = true;
    }

    public V GetDescriptor(ResourceHandle<T> handle)
    {
        if (handle.Index < 0)
            return importedDescriptors[-handle.Index];

        var descriptors = handle.IsPersistent ? persistentDescriptors : this.descriptors;
        return descriptors[handle.Index];
    }

    public void SetDescriptor(ResourceHandle<T> handle, V descriptor)
    {
        if (handle.Index < 0)
        {
            importedDescriptors[-handle.Index] = descriptor;
            return;
        }

        var descriptors = handle.IsPersistent ? persistentDescriptors : this.descriptors;
        descriptors[handle.Index] = descriptor;
    }

    public void AllocateFrameResources(int renderPassCount, int frameIndex)
    {
        List<List<ResourceHandle<T>>> handlesToCreate = new();
        List<List<ResourceHandle<T>>> handlesToFree = new();

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
                var descriptor = GetDescriptor(handle);
                var resourceIndex = -1;
                for (var j = 0; j < resources.Count; j++)
                {
                    if (!isAvailable[j])
                        continue;

                    if (lastFrameUsed[j] > frameIndex)
                        continue;

                    var resource = resources[j];
                    if (!DoesResourceMatchDescriptor(resource, descriptor))
                        continue;

                    resourceIndex = j;
                    break;
                }

                if (resourceIndex == -1)
                {
                    var result = descriptor.CreateResource();

                    // Get a slot for this render texture if possible
                    if (!availableSlots.TryPop(out resourceIndex))
                    {
                        resourceIndex = resources.Count;
                        resources.Add(result);
                        lastFrameUsed.Add(-1);
                        isAvailable.Add(false);
                    }
                    else
                    {
                        resources[resourceIndex] = result;
                    }
                }
                else
                {
                    isAvailable[resourceIndex] = false;
                }

                // Persistent handle no longer needs to be created or cleared. (Non-persistent create list gets cleared every frame)
                if (handle.IsPersistent)
                {
                    isAssigned[handle.Index] = true;
                    persistentCreateList[handle.Index] = -1;
                    persistentResourceIndices[handle.Index] = resourceIndex;
                }
                else
                {
                    resourceIndices[handle.Index] = resourceIndex;
                }
            }

            // Now mark any textures that need to be released at the end of this pass as available
            foreach (var handle in handlesToFree[i])
            {
                // Todo: too much indirection?
                var resourceIndex = handle.IsPersistent ? persistentResourceIndices[handle.Index] : resourceIndices[handle.Index];
                lastFrameUsed[resourceIndex] = frameIndex + ExtraFramesToKeepResource(resources[resourceIndex]);
                isAvailable[resourceIndex] = true;

                // If non persistent, no additional logic required since it will be re-created, but persistent needs to free its index
                if (handle.IsPersistent)
                {
                    availablePersistentHandleIndices.Push(handle.Index);
                    persistentFreeList[handle.Index] = -1; // Set to -1 to indicate this doesn't need to be freed again
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
            if (lastFrameUsed[i] >= frameIndex)
                continue;

            Debug.LogWarning($"Destroying resource {resources[i]} at index {i}");

            DestroyResource(resources[i]);

            isAvailable[i] = false;
            availableSlots.Push(i);
        }

        handles.Clear();
        descriptors.Clear();
        resourceIndices.Clear();
        createList.Clear();
        freeList.Clear();
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
            if (resource != null)
            {
                // Persistent resources should be freed first
                //if (!isAvailable[i])
                    //Debug.LogError($"Resource at index {i} was not made availalble");

                DestroyResource(resource);
            }
        }

        disposedValue = true;
    }
}
