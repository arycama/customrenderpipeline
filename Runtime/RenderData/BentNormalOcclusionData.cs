using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class BentNormalOcclusionData : RTHandleData
    {
        public BentNormalOcclusionData(ResourceHandle<RenderTexture> handle) : base(handle, "_BentNormalOcclusion")
        {
        }
    }
}