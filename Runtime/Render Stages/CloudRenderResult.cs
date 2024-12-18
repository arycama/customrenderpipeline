using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct CloudRenderResult : IRenderPassData
    {
        private readonly RTHandle cloudTexture, cloudTransmittanceTexture, cloudDepth;

        public CloudRenderResult(RTHandle cloudLuminanceTexture, RTHandle cloudTransmittanceTexture, RTHandle cloudDepth)
        {
            this.cloudTexture = cloudLuminanceTexture ?? throw new ArgumentNullException(nameof(cloudLuminanceTexture));
            this.cloudTransmittanceTexture = cloudTransmittanceTexture;
            this.cloudDepth = cloudDepth ?? throw new ArgumentNullException(nameof(cloudDepth));
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("CloudTexture", cloudTexture);
            pass.ReadTexture("CloudTransmittanceTexture", cloudTransmittanceTexture);
            pass.ReadTexture("CloudDepthTexture", cloudDepth);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector("CloudTextureScaleLimit", cloudTexture.ScaleLimit2D);
            pass.SetVector("CloudTransmittanceTextureScaleLimit", cloudTransmittanceTexture.ScaleLimit2D);
        }
    }
}
