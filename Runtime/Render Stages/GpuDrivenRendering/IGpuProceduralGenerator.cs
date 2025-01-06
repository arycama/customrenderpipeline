namespace Arycama.CustomRenderPipeline
{
    public interface IGpuProceduralGenerator
    {
        int Version { get; }
        void Generate(RenderGraph renderGraph);
    }
}