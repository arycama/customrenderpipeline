using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class HiZMaxDepthData : RTHandleData
    {
        public HiZMaxDepthData(ResourceHandle<RenderTexture> handle) : base(handle, "_HiZMaxDepth")
        {
        }
    }
}