using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class CopyTexturePass : RenderPassBase
    {
        private ResourceHandle<RenderTexture> source, dest;

        public void Initialize(ResourceHandle<RenderTexture> source, ResourceHandle<RenderTexture> dest)
        {
            this.source = source;
            this.dest = dest;

            RenderGraph.RtHandleSystem.ReadResource(source, Index);
            RenderGraph.RtHandleSystem.WriteResource(dest, Index);
        }

        protected override void Execute()
        {
            var srcDesc = RenderGraph.RtHandleSystem.GetDescriptor(source);
            Command.CopyTexture(GetRenderTexture(source), 0, 0, 0, 0, srcDesc.Width, srcDesc.Height, GetRenderTexture(dest), 0, 0, 0, 0);
        }
    }
}