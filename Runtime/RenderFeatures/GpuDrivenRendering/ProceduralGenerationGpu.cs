using UnityEngine.Pool;
using UnityEngine.Rendering;

public class ProceduralGenerationGpu : FrameRenderFeature
{
    private readonly ProceduralGenerationController controller;

    public ProceduralGenerationGpu(RenderGraph renderGraph, ProceduralGenerationController controller) : base(renderGraph)
    {
        this.controller = controller;

        // Reset so that generators are re-generated
        controller.Reset();
    }

    public override void Render(ScriptableRenderContext context)
    {
		using var scope = ListPool<IGpuProceduralGenerator>.Get(out var list);
		controller.GetModifiedGenerators(list);

		foreach (var generator in list)
        {
            generator.Generate(renderGraph);
        }
    }
}