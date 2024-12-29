using System;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public abstract partial class TerrainRendererBase
    {
        public readonly struct CullResult
        {
            public ResourceHandle<GraphicsBuffer> IndirectArgsBuffer { get; }
            public ResourceHandle<GraphicsBuffer> PatchDataBuffer { get; }

            public CullResult(ResourceHandle<GraphicsBuffer> indirectArgsBuffer, ResourceHandle<GraphicsBuffer> patchDataBuffer)
            {
                IndirectArgsBuffer = indirectArgsBuffer;
                PatchDataBuffer = patchDataBuffer;
            }
        }
    }
}