using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

public abstract class ResourceHandleSystem<T, V> : ResourceHandleSystemBase, IDisposable where T : class where V : IResourceDescriptor<T>
{
	private readonly FreeList<ResourceHandleData<V, T>> handleInfo = new();
	private readonly FreeList<(T resource, int lastFrameUsed, bool isAvailable)> resources = new();
	private readonly Dictionary<T, ResourceHandle<T>> importedResourceLookup = new();
	private readonly List<int> handlesToFree = new();
	private bool disposedValue;

	private readonly List<List<int>> frameHandlesToCreate = new();
	private readonly List<List<int>> frameHandlesToFree = new();

	~ResourceHandleSystem()
	{
		Dispose(false);
	}

	public ResourceHandle<T> GetResourceHandle(V descriptor, bool isPersistent = false)
	{
		var handleIndex = handleInfo.Add(new(-1, -1, -1, descriptor, false, isPersistent, true));
		return new ResourceHandle<T>(handleIndex);
	}

	public ResourceHandle<T> ImportResource(T resource)
	{
		if (importedResourceLookup.TryGetValue(resource, out var result))
			return result;

		var resourceIndex = resources.Add((resource, -1, false));
		var descriptor = CreateDescriptorFromResource(resource);
		var handleIndex = handleInfo.Add(new(-1, -1, resourceIndex, descriptor, false, true, true));
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

		// Persistent handles do not get freed automatically so there is no work to do
		if (info.isPersistent)
			return;

		info.freeIndex = info.freeIndex == -1 ? passIndex : Math.Max(info.freeIndex, passIndex);
		handleInfo[handle.Index] = info;
	}

	public T GetResource(ResourceHandle<T> handle)
	{
		var resourceIndex = handleInfo[handle.Index].resourceIndex;
		return resources[resourceIndex].resource;
	}

	public void ReleasePersistentResource(ResourceHandle<T> handle, int passIndex)
	{
		var info = handleInfo[handle.Index];
		Assert.IsTrue(info.isPersistent);
		info.freeIndex = info.freeIndex == -1 ? passIndex : Math.Max(info.freeIndex, passIndex);
		info.isPersistent = false;
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

		// Ensure capacity
		for (var i = frameHandlesToCreate.Count; i < renderPassCount; i++)
			frameHandlesToCreate.Add(new());

		// Ensure capacity
		for (var i = frameHandlesToFree.Count; i < renderPassCount; i++)
			frameHandlesToFree.Add(new());

		// Add handles to create/free lists if needed
		for (var i = 0; i < handleInfo.Count; i++)
		{
			var resourceHandleData = handleInfo[i];

			if (!resourceHandleData.isUsed)
				continue;

			if (resourceHandleData.createIndex != -1)
				frameHandlesToCreate[resourceHandleData.createIndex].Add(i);

			if (resourceHandleData.freeIndex != -1)
				frameHandlesToFree[resourceHandleData.freeIndex].Add(i);
			else if (!resourceHandleData.isPersistent && resourceHandleData.createIndex != -1)
			{
				// If the resource is not used, mark it as available immediately. TODO: Instead we should avoid running renderpasses entirely if their outputs are not used
				// However a rnederpass might produce multiple outputs, some of which are read, and others which aren't, so we may still end up with some unused outputs.
				frameHandlesToFree[resourceHandleData.createIndex].Add(i);
			}
		}

		for (var i = 0; i < renderPassCount; i++)
		{
			// Assign or create any RTHandles that are written to by this pass
			foreach (var handle in frameHandlesToCreate[i])
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

			frameHandlesToCreate[i].Clear();

			// Now mark any textures that need to be released at the end of this pass as available
			foreach (var handle in frameHandlesToFree[i])
			{
				var resourceHandleData = handleInfo[handle];

				// Could handle this by updating the last used index or something maybe
				var resource = resources[resourceHandleData.resourceIndex];
				resources[resourceHandleData.resourceIndex] = (resource.resource, frameIndex, true);

				this.handlesToFree.Add(handle);
			}

			frameHandlesToFree[i].Clear();
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

			//Debug.LogWarning($"Destroying resource {resources[i]} at index {i}");

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