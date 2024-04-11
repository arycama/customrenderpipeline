using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public interface IRenderPassData
    {
        public void SetInputs(RenderPass pass);
        public void SetProperties(RenderPass pass, CommandBuffer command);
    }

    // Just used to identify a single type that contains common data
    public interface ICommonPassData : IRenderPassData
    {
    }
}