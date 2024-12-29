using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class CameraDepthData : RTHandleData
    {
        public CameraDepthData(ResourceHandle<RenderTexture> handle) : base(handle, "_Depth")
        {
        }
    }
}