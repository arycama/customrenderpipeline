using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct PreviousFrameDepth : IRenderPassData
    {
        private readonly RTHandle previousDepth;

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