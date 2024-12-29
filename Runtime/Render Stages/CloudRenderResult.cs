using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct CloudRenderResult : IRenderPassData
    {
        private readonly RTHandle cloudTexture, cloudTransmittanceTexture, cloudDepth;

        public CloudRenderResult(RTHandle cloudLuminanceTexture, RTHandle cloudTransmittanceTexture, RTHandle cloudDepth)
        {
            cloudTexture = cloudLuminanceTexture;
            this.cloudTransmittanceTexture = cloudTransmittanceTexture;
            this.cloudDepth = cloudDepth;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("CloudTexture", cloudTexture);
            pass.ReadTexture("CloudTransmittanceTexture", cloudTransmittanceTexture);
            pass.ReadTexture("CloudDepthTexture", cloudDepth);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector("CloudTextureScaleLimit", pass.GetScaleLimit2D(cloudTexture));
            pass.SetVector("CloudTransmittanceTextureScaleLimit", pass.GetScaleLimit2D(cloudTransmittanceTexture));
        }
    }
}
