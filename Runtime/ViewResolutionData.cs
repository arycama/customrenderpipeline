using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public struct ViewResolutionData : IRenderPassData
    {
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public int ScaledWidth { get; }
        public int ScaledHeight { get; }

        public ViewResolutionData(int pixelWidth, int pixelHeight, int scaledWidth, int scaledHeight)
        {
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            ScaledWidth = scaledWidth;
            ScaledHeight = scaledHeight;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}