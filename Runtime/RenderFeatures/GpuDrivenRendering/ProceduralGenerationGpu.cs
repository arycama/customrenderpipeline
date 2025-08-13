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
        foreach(var generator in controller.GetModifiedGenerators())
        {
            generator.Generate(renderGraph);
        }
    }
}