using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct PreviousFrameVelocity : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> previousVelocity;

        public PreviousFrameVelocity(ResourceHandle<RenderTexture> previousVelocity)
        {
            this.previousVelocity = previousVelocity;
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