using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    /// <summary> Render feature that executes once per frame </summary>
    public abstract class FrameRenderFeature : RenderFeatureBase
    {
        public FrameRenderFeature(RenderGraph renderGraph) : base(renderGraph) { }

        public abstract void Render(ScriptableRenderContext context);
    }
}