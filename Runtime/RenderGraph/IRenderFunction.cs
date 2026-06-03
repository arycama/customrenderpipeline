using UnityEngine.Rendering;

namespace CustomRenderPipeline
{
    public interface IRenderFunction
    {
        public abstract void Execute(CommandBuffer command, RenderPass pass, object data);
    }
}