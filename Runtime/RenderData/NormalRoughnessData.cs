using UnityEngine;
namespace Arycama.CustomRenderPipeline
{
    public class NormalRoughnessData : RTHandleData
    {
        public NormalRoughnessData(ResourceHandle<RenderTexture> handle) : base(handle, "_NormalRoughness")
        {
        }
    }
}