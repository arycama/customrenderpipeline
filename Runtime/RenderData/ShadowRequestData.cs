using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ShadowRequestData : IRenderPassData
    {
        public ShadowRequest ShadowRequest { get; }
        public float Bias { get; }
        public float SlopeBias { get; }
        public ResourceHandle<RenderTexture> Shadow { get; }
        public int CascadeIndex { get; }

        public ShadowRequestData(ShadowRequest shadowRequest, float bias, float slopBias, ResourceHandle<RenderTexture> shadow, int cascadeIndex)
        {
            ShadowRequest = shadowRequest;
            Bias = bias;
            SlopeBias = slopBias;
            Shadow = shadow;
            CascadeIndex = cascadeIndex;
        }

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}