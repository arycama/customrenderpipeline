﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class PersistentRTHandleCache : IDisposable
    {
        private readonly Dictionary<int, RTHandle> textureCache = new();

        private readonly GraphicsFormat format;
        private readonly TextureDimension dimension;

        private readonly bool hasMips;
        private readonly string name;

        public RenderGraph renderGraph;
        private bool disposedValue;

        public PersistentRTHandleCache(GraphicsFormat format, RenderGraph renderGraph, string name = "", TextureDimension dimension = TextureDimension.Tex2D, bool hasMips = false)
        {
            this.format = format;
            this.dimension = dimension;
            this.renderGraph = renderGraph;
            this.name = name;
            this.hasMips = hasMips;
        }

        // Gets current texture and marks history as non-persistent
        public (RTHandle current, RTHandle history, bool wasCreated) GetTextures(int width, int height, int viewIndex, bool isScreenTexture = true, int depth = 1)
        {
            var wasCreated = !textureCache.TryGetValue(viewIndex, out var history);
            if (wasCreated)
            {
                switch (dimension)
                {
                    case TextureDimension.Tex2D:
                        history = renderGraph.EmptyTexture;
                        break;
                    case TextureDimension.Tex3D:
                        history = renderGraph.Empty3DTexture;
                        break;
                    case TextureDimension.Cube:
                        history = renderGraph.EmptyCubemap;
                        break;
                    case TextureDimension.Tex2DArray:
                        history = renderGraph.EmptyTextureArray;
                        break;
                    case TextureDimension.CubeArray:
                        history = renderGraph.EmptyCubemapArray;
                        break;
                    default:
                        throw new NotSupportedException(dimension.ToString());
                }
            }
            else
                history.IsReleasable = false;

            var current = renderGraph.GetTexture(width, height, format, depth, dimension, isScreenTexture, hasMips, isPersistent: true);
            textureCache[viewIndex] = current;

            return (current, history, wasCreated);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue)
                return;

            foreach (var texture in textureCache)
                texture.Value.IsReleasable = false;

            if (!disposing)
                Debug.LogError($"Persistent RT Handle Cache [{name}] not disposed correctly");

            disposedValue = true;
        }

        ~PersistentRTHandleCache()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}