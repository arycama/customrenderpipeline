using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class HiZMinDepthData : RTHandleData
    {
        public HiZMinDepthData(ResourceHandle<RenderTexture> handle) : base(handle, "_HiZMinDepth")
        {
        }
    }
}