using Arycama.CustomRenderPipeline;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class ProceduralGenerationController
{
    private readonly List<(IGpuProceduralGenerator, int)> generatorData = new();

    private readonly List<GameObject> prefabs = new();
    private readonly List<ResourceHandle<GraphicsBuffer>> positionBuffers = new();
    private readonly List<ResourceHandle<GraphicsBuffer>> instanceIdBuffers = new();
    private readonly List<int> counts = new();
    private readonly List<ResourceHandle<GraphicsBuffer>> activeHandles = new();
    private readonly Stack<int> freeHandleIndices = new();

    private readonly List<ResourceHandle<GraphicsBuffer>> handlesToFree = new();

    private int pendingRequests;

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
    }

    public void Reset()
    {
        // Reset all generators so they will be regenerated
        for (var i = 0; i < generatorData.Count; i++)
        {
            generatorData[i] = (generatorData[i].Item1, -1);
        }
    }

    public void AddData(ResourceHandle<GraphicsBuffer> positionBuffer, ResourceHandle<GraphicsBuffer> instanceIdBuffer, IList<GameObject> gameObjects)
    {
        positionBuffers.Add(positionBuffer);
        instanceIdBuffers.Add(instanceIdBuffer);
        counts.Add(-1);

        for (var i = 0; i < gameObjects.Count; i++)
            prefabs.Add(gameObjects[i]);
    }

    public void OnRequestComplete(AsyncGPUReadbackRequest request, int index)
    {
        var count = request.GetData<int>()[0];
        counts[index] = count;

        handlesToFree.Add(activeHandles[index]);
        freeHandleIndices.Push(index);

        pendingRequests--;
    }

    /// <summary>
    /// Queues a handle for freeing once a readback is complete
    /// </summary>
    /// <param name="handle">The handle to release</param>
    /// <returns>The index of the handle</returns>
    public int AddHandleToFree(ResourceHandle<GraphicsBuffer> handle)
    {
        if(!freeHandleIndices.TryPop(out var index))
        {
            index = freeHandleIndices.Count;
            activeHandles.Add(handle);
        }
        else
        {
            activeHandles[index] = handle;
        }

        pendingRequests++;
        return index;
    }

    public void FreeUnusedHandles(RenderGraph renderGraph)
    {
        foreach(var handle in handlesToFree)
        {
            renderGraph.ReleasePersistentResource(handle);
        }

        handlesToFree.Clear();
    }

    public void Generate()
    {
        if (pendingRequests > 0)
            return;
    }
}
