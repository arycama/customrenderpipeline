using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct PreviousFrameVelocity : IRenderPassData
    {
        private readonly RTHandle previousVelocity;

        public PreviousFrameVelocity(RTHandle previousVelocity)
        {
            this.previousVelocity = previousVelocity ?? throw new ArgumentNullException(nameof(previousVelocity));
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("PreviousVelocity", previousVelocity);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}