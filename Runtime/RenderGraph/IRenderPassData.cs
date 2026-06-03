using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public interface IRenderPassData
    {
        public void SetInputs(RenderPass pass);
        public void SetProperties(RenderPass pass, CommandBuffer command);
    }
}