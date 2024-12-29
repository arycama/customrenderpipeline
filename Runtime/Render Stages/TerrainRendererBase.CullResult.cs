using System;

namespace Arycama.CustomRenderPipeline
{
    public abstract partial class TerrainRendererBase
    {
        public readonly struct CullResult
        {
            public BufferHandle IndirectArgsBuffer { get; }
            public BufferHandle PatchDataBuffer { get; }

            public CullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer)
            {
                IndirectArgsBuffer = indirectArgsBuffer;
                PatchDataBuffer = patchDataBuffer;
            }
        }
    }
}