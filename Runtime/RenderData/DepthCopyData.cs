using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class DepthCopyData : RTHandleData
    {
        public DepthCopyData(ResourceHandle<RenderTexture> handle) : base(handle, "_DepthCopy")
        {
        }
    }
}