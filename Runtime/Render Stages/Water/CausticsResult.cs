using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public struct CausticsResult : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> caustics;
        private readonly int cascade;
        private readonly float depth;

        public CausticsResult(ResourceHandle<RenderTexture> caustics, int cascade, float depth)
        {
            this.caustics = caustics;
            this.cascade = cascade;
            this.depth = depth;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("OceanCaustics", caustics);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetFloat("CausticsCascade", cascade);
            pass.SetFloat("CausticsDepth", depth);
        }
    }
}