using System;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderFeature : IDisposable
    {
        protected RenderGraph renderGraph;
        private bool disposedValue;

        public RenderFeature(RenderGraph renderGraph)
        {
            this.renderGraph = renderGraph ?? throw new ArgumentNullException(nameof(renderGraph));
        }

        protected virtual void Cleanup(bool disposing)
        {
            // Override in derived classes and put any cleanup code here (Eg free buffers, RT handles etc)
        }

        private void Dispose(bool disposing)
        {
            if (disposedValue)
                return;

            Cleanup(disposing);
            disposedValue = true;
        }

        ~RenderFeature()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}