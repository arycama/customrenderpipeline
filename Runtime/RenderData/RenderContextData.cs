using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class RenderContextData : IRenderPassData
    {
        public ScriptableRenderContext Context { get; }

        public RenderContextData(ScriptableRenderContext context) => Context = context;

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}