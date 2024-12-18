using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public abstract class RenderFeatureBase : IDisposable
    {
        protected readonly RenderGraph renderGraph;
        private bool disposedValue;

        public RenderFeatureBase(RenderGraph renderGraph)
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

            if (!disposing)
                Debug.LogError($"Render Feature [{GetType()}] not disposed correctly");

            Cleanup(disposing);
            disposedValue = true;
        }

        ~RenderFeatureBase()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public abstract class RenderFeature : RenderFeatureBase
    {
        protected RenderFeature(RenderGraph renderGraph) : base(renderGraph)
        {
        }

        /// <summary>
        /// Render logic goes here
        /// </summary>
        public abstract void Render();
    }

    public abstract class RenderFeature<T> : RenderFeatureBase
    {
        protected RenderFeature(RenderGraph renderGraph) : base(renderGraph)
        {
        }

        /// <summary>
        /// Render logic goes here
        /// </summary>
        public abstract void Render(T data);
    }
}