using System;

namespace Arycama.CustomRenderPipeline
{
    public class GenericRenderFeature : RenderFeature
    {
        private readonly Action render;

        public GenericRenderFeature(RenderGraph renderGraph, Action render) : base(renderGraph)
        {
            this.render = render ?? throw new ArgumentNullException(nameof(render));
        }

        public override void Render()
        {
            render();
        }
    }
}