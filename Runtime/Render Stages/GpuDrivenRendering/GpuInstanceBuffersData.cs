using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class GpuInstanceBuffersData : IRenderPassData
    {
        public GpuInstanceBuffers Data { get; }

        public GpuInstanceBuffersData(GpuInstanceBuffers data)
        {
            Data = data;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}