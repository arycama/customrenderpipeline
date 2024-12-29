using System;

namespace Arycama.CustomRenderPipeline.Water
{
    public readonly struct WaterCullResult
    {
        public BufferHandle IndirectArgsBuffer { get; }
        public BufferHandle PatchDataBuffer { get; }

        public WaterCullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer)
        {
            IndirectArgsBuffer = indirectArgsBuffer;
            PatchDataBuffer = patchDataBuffer;
        }
    }
}