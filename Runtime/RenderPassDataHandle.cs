namespace Arycama.CustomRenderPipeline
{
    public readonly struct RenderPassDataHandle
    {
        public int Index { get; }

        public RenderPassDataHandle(int index)
        {
            Index = index;
        }
    }
}
