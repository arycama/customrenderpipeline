using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class PersistentRTHandleCache
    {
        private GraphicsFormat format;
        private Dictionary<Camera, RTHandle> textureCache = new();
        private string name;

        public RenderGraph renderGraph;

        public PersistentRTHandleCache(GraphicsFormat format, RenderGraph renderGraph, string name = "")
        {
            this.format = format;
            this.renderGraph = renderGraph;
            this.name = name;
        }

        // Gets current texture and marks history as non-persistent
        public (RTHandle current, RTHandle history, bool wasCreated) GetTextures(int width, int height, bool isScreenTexture, Camera camera)
        {
            var wasCreated = !textureCache.TryGetValue(camera, out var history);
            if (wasCreated)
                history = renderGraph.EmptyTexture;
            else
                history.IsPersistent = false;

            var current = renderGraph.GetTexture(width, height, format, isScreenTexture: isScreenTexture, isPersistent: true);
            textureCache[camera] = current;

            return (current, history, wasCreated);
        }
    }
}