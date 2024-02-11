using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public interface IRenderPassData
    {
        public void SetInputs(RenderPass pass);
        public void SetProperties(RenderPass pass, CommandBuffer command);
    }
}