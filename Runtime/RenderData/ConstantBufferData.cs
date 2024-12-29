using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public abstract class ConstantBufferData : IRenderPassData
    {
        private readonly ResourceHandle<GraphicsBuffer> buffer;
        private readonly string propertyName;

        public ConstantBufferData(ResourceHandle<GraphicsBuffer> buffer, string propertyName)
        {
            this.buffer = buffer;
            this.propertyName = propertyName;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadBuffer(propertyName, buffer);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}