using System;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct RenderPassDataHandle
    {
        public int Index { get; }

        // For debugging only, can compile out
        public Type Type { get; }

        public RenderPassDataHandle(int index, Type type)
        {
            Index = index;
            Type = type;
        }
    }
}
