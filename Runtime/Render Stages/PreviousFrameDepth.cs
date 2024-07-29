using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public struct PreviousFrameDepth : IRenderPassData
    {
        private RTHandle previousDepth;

        public PreviousFrameDepth(RTHandle previousDepth)
        {
            this.previousDepth = previousDepth ?? throw new ArgumentNullException(nameof(previousDepth));
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("PreviousDepth", previousDepth);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}