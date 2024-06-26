﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class PersistentRTHandleCache
    {
        private Dictionary<Camera, RTHandle> textureCache = new();

        private GraphicsFormat format;
        private TextureDimension dimension;

        private bool hasMips;
        private string name;

        public RenderGraph renderGraph;

        public PersistentRTHandleCache(GraphicsFormat format, RenderGraph renderGraph, string name = "", TextureDimension dimension = TextureDimension.Tex2D, bool hasMips = false)
        {
            this.format = format;
            this.dimension = dimension;
            this.renderGraph = renderGraph;
            this.name = name;
            this.hasMips = hasMips;
        }

        // Gets current texture and marks history as non-persistent
        public (RTHandle current, RTHandle history, bool wasCreated) GetTextures(int width, int height, Camera camera, bool isScreenTexture = true, int depth = 1)
        {
            var wasCreated = !textureCache.TryGetValue(camera, out var history);
            if (wasCreated)
            {
                switch(dimension)
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
                history.IsPersistent = false;

            var current = renderGraph.GetTexture(width, height, format, depth, dimension, isScreenTexture, hasMips, isPersistent: true);
            textureCache[camera] = current;

            return (current, history, wasCreated);
        }
    }
}