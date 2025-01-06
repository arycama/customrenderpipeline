using Arycama.CustomRenderPipeline;
using System.Collections.Generic;
using UnityEngine.Assertions;

public class ProceduralGenerationController
{
    private readonly List<(IGpuProceduralGenerator, int)> generatorData = new();

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
}
