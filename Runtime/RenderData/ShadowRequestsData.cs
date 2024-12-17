using System.Collections.Generic;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ShadowRequestsData : IRenderPassData
    {
        public List<ShadowRequest> DirectionalShadowRequests { get; }
        public List<ShadowRequest> PointShadowRequests { get; }

        public ShadowRequestsData(List<ShadowRequest> directionalShadowRequests, List<ShadowRequest> pointShadowRequests)
        {
            DirectionalShadowRequests = directionalShadowRequests;
            PointShadowRequests = pointShadowRequests;
        }

        public void SetInputs(RenderPass pass)
        {
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}