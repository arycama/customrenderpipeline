using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct PreviousFrameDepth : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> previousDepth;

        public PreviousFrameDepth(ResourceHandle<RenderTexture> previousDepth)
        {
            this.previousDepth = previousDepth;
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