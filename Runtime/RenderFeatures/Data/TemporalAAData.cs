using UnityEngine;
using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public readonly struct TemporalAAData : IRenderPassData
    {
        private readonly ResourceHandle<GraphicsBuffer> buffer;

        public TemporalAAData(ResourceHandle<GraphicsBuffer> buffer)
        {
            this.buffer = buffer;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("TemporalProperties", buffer);
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}