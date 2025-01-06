using System.Collections.Generic;
using UnityEngine.Assertions;

namespace Arycama.CustomRenderPipeline
{
    public class ProceduralGenerationGpu : RenderFeature
    {
        private readonly ProceduralGenerationController controller;

        public ProceduralGenerationGpu(RenderGraph renderGraph, ProceduralGenerationController controller) : base(renderGraph)
        {
            this.controller = controller;

            // Reset so that generators are re-generated
            controller.Reset();
        }

        public override void Render()
        {
            foreach(var generator in controller.GetModifiedGenerators())
            {
                generator.Generate(renderGraph);
            }
        }
    }
}