using System;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderFeature
    {
        protected RenderGraph renderGraph;

        public RenderFeature(RenderGraph renderGraph)
        {
            this.renderGraph = renderGraph ?? throw new ArgumentNullException(nameof(renderGraph));
        }
    }
}