using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct CloudShadowDataResult : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> cloudShadow;
        private readonly float cloudDepthInvScale, cloudShadowExtinctionInvScale;
        private readonly Matrix4x4 worldToCloudShadow;
        private readonly ResourceHandle<GraphicsBuffer> cloudCoverageBuffer;
        private readonly float startHeight, endHeight;

        public CloudShadowDataResult(ResourceHandle<RenderTexture> cloudShadow, float cloudDepthInvScale, Matrix4x4 worldToCloudShadow, float cloudShadowExtinctionInvScale, ResourceHandle<GraphicsBuffer> cloudCoverageBuffer, float startHeight, float endHeight)
        {
            this.cloudShadow = cloudShadow;
            this.cloudDepthInvScale = cloudDepthInvScale;
            this.worldToCloudShadow = worldToCloudShadow;
            this.cloudShadowExtinctionInvScale = cloudShadowExtinctionInvScale;
            this.cloudCoverageBuffer = cloudCoverageBuffer;
            this.startHeight = startHeight;
            this.endHeight = endHeight;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_CloudShadow", cloudShadow);
            pass.ReadBuffer("CloudCoverage", cloudCoverageBuffer);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetMatrix("_WorldToCloudShadow", worldToCloudShadow);
            pass.SetFloat("_CloudShadowDepthInvScale", cloudDepthInvScale);
            pass.SetFloat("_CloudShadowExtinctionInvScale", cloudShadowExtinctionInvScale);
            pass.SetFloat("_CloudCoverageStart", cloudShadowExtinctionInvScale);
            pass.SetFloat("_CloudShadowExtinctionInvScale", cloudShadowExtinctionInvScale);

            // This is used to scale a smooth falloff that uses distance^2
            var cloudCoverageScale = 1.0f / (startHeight * startHeight - endHeight * endHeight);
            var cloudCoverageOffset = -endHeight * endHeight / (startHeight * startHeight - endHeight * endHeight);
            pass.SetFloat("_CloudCoverageScale", cloudCoverageScale);
            pass.SetFloat("_CloudCoverageOffset", cloudCoverageOffset);

            pass.SetVector("_CloudShadowScaleLimit", pass.GetScaleLimit2D(cloudShadow));
        }
    }
}
