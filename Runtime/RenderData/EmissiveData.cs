using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class EmissiveData : RTHandleData
    {
        public EmissiveData(ResourceHandle<RenderTexture> handle) : base(handle, "_Emissive")
        {
        }
    }
}