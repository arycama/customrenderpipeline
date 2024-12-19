using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderFeature : IDisposable
    {
        protected readonly RenderGraph renderGraph;
        private bool disposedValue;

        public RenderFeature(RenderGraph renderGraph)
        {
            this.renderGraph = renderGraph ?? throw new ArgumentNullException(nameof(renderGraph));
        }

        ~RenderFeature()
        {
            Dispose(disposing: false);
        }

        public abstract void Render();

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        // Override in derived classes and put any cleanup code here (Eg free buffers, RT handles etc)
        protected virtual void Cleanup(bool disposing)
        {
        }

        private void Dispose(bool disposing)
        {
            if (disposedValue)
                return;

            if (!disposing)
                Debug.LogError($"Render Feature [{GetType()}] not disposed correctly");

            Cleanup(disposing);
            disposedValue = true;
        }
    }
}