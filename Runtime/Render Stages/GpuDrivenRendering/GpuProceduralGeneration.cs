using System.Collections.Generic;
using UnityEngine.Assertions;

namespace Arycama.CustomRenderPipeline
{
    public class GpuProceduralGeneration : RenderFeature
    {
        private readonly List<(IGpuProceduralGenerator, int)> generatorVersions = new();

        public GpuProceduralGeneration(RenderGraph renderGraph) : base(renderGraph)
        {
        }

        public void AddGenerator(IGpuProceduralGenerator generator)
        {
            Assert.AreEqual(generatorVersions.FindIndex(x => x.Item1 == generator), -1, "Trying to add the same generator more than once");
            generatorVersions.Add((generator, -1));
        }

        public void RemoveGenerator(IGpuProceduralGenerator generator)
        {
            var index = generatorVersions.FindIndex(x => x.Item1 == generator);
            Assert.AreNotEqual(index, -1, "Trying to remove a generator that was not added");
            generatorVersions.RemoveAt(index);
        }

        public override void Render()
        {
            // Check each generator to see if it's version has changed, if so, udpate it
            for(var i = 0; i < generatorVersions.Count; i++)
            {
                var element = generatorVersions[i];
                var version = element.Item1.Version;

                if (version == element.Item2)
                    continue;

                using (var renderPass = renderGraph.AddRenderPass<GlobalRenderPass>("Procedural Generation"))
                {
                    renderPass.SetRenderFunction((command, pass) =>
                    {
                        element.Item1.Generate(command, renderGraph);
                    });
                }

                // Update the index
                generatorVersions[i] = (element.Item1, version);
            }
        }
    }
}