using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class PreviousFrameColorData : RTHandleData
    {
        public PreviousFrameColorData(ResourceHandle<RenderTexture> handle) : base(handle, "PreviousFrame")
        {
        }
    }
}