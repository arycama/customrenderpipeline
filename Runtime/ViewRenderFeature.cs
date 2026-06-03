using System;
using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    /// <summary> Render feature that executes once per camera </summary>
    public abstract class ViewRenderFeature : RenderFeatureBase
    {
        public ViewRenderFeature(RenderGraph renderGraph) : base(renderGraph) { }

        public abstract void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData displayRenderPass, in DisplayData displayOutputData, ScriptableRenderContext context);
    }
}