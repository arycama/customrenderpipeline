using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class ProceduralGenerationController
{
    public readonly List<(GameObject prefab, int count)> prefabs = new();
    public readonly List<(ResourceHandle<GraphicsBuffer> positions, ResourceHandle<GraphicsBuffer> instanceId, int totalCount)> instanceData = new();

    private readonly List<(IGpuProceduralGenerator, int)> generatorData = new();
    private readonly FreeList<ResourceHandle<GraphicsBuffer>> activeHandles = new();
    private readonly List<ResourceHandle<GraphicsBuffer>> handlesToFree = new();

    private int pendingRequests;

    public int Version { get; private set; } = -1;

    public void Reset()
    {
        // Reset all generators so they will be regenerated
        for (var i = 0; i < generatorData.Count; i++)
        {
            generatorData[i] = (generatorData[i].Item1, -1);
        }

        prefabs.Clear();
        instanceData.Clear();
        activeHandles.Clear();
        handlesToFree.Clear();

        Version = -1;
    }

    public IEnumerable<IGpuProceduralGenerator> GetModifiedGenerators()
    {
        // Check each generator to see if it's version has changed, if so, udpate it
        for (var i = 0; i < generatorData.Count; i++)
        {
            var element = generatorData[i];
            var version = element.Item1.Version;

            if (version == element.Item2)
                continue;

            yield return element.Item1;

            generatorData[i] = (element.Item1, version);
        }
    }

    public void AddGenerator(IGpuProceduralGenerator generator)
    {
        Assert.AreEqual(generatorData.FindIndex(x => x.Item1 == generator), -1, "Trying to add the same generator more than once");
        generatorData.Add((generator, -1));
    }

    public void RemoveGenerator(IGpuProceduralGenerator generator)
    {
        var index = generatorData.FindIndex(x => x.Item1 == generator);
        Assert.AreNotEqual(index, -1, "Trying to remove a generator that was not added");
        generatorData.RemoveAt(index);
		Version++;
	}

    public int AddData(ResourceHandle<GraphicsBuffer> positionBuffer, ResourceHandle<GraphicsBuffer> instanceIdBuffer, IList<GameObject> gameObjects)
    {
        instanceData.Add((positionBuffer, instanceIdBuffer, -1));

        var startIndex = prefabs.Count;
        for (var i = 0; i < gameObjects.Count; i++)
            prefabs.Add((gameObjects[i], -1));

        return startIndex;
    }

	public void OnRequestComplete(NativeArray<int> counts, int handleIndex, int prefabStartIndex, int prefabCount)
	{
		var totalCount = 0;
		for (var i = 0; i < prefabCount; i++)
		{
			var index = prefabStartIndex + i;
			var data = prefabs[index];

			var count = counts[i];
			data.count = count;
			totalCount += count;

			prefabs[index] = data;
		}

		var passData = instanceData[handleIndex];
		passData.totalCount = totalCount;
		instanceData[handleIndex] = passData;

		handlesToFree.Add(activeHandles[handleIndex]);
		activeHandles.Free(handleIndex);

		pendingRequests--;

		if (pendingRequests == 0)
			Version++;
	}


	/// <summary>
	/// Writes the counts from the GPU readback into each prefab buffer, and frees the handle index for the buffer
	/// </summary>
	/// <param name="request"></param>
	/// <param name="handleIndex"></param>
	/// <param name="prefabStartIndex"></param>
	/// <param name="prefabCount"></param>
	public void OnRequestComplete(AsyncGPUReadbackRequest request, int handleIndex, int prefabStartIndex, int prefabCount)
    {
		var counts = request.GetData<int>();
		OnRequestComplete(counts, handleIndex, prefabStartIndex, prefabCount);
    }

    /// <summary>
    /// Queues a handle for freeing once a readback is complete
    /// </summary>
    /// <param name="handle">The handle to release</param>
    /// <returns>The index of the handle</returns>
    public int AddHandleToFree(ResourceHandle<GraphicsBuffer> handle)
    {
        pendingRequests++;
        return activeHandles.Add(handle);
    }

    public void FreeUnusedHandles(RenderGraph renderGraph)
    {
        foreach(var handle in handlesToFree)
        {
            renderGraph.ReleasePersistentResource(handle);
        }

        handlesToFree.Clear();
    }
}
