using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class UITextureData : RTHandleData
    {
        public UITextureData(ResourceHandle<RenderTexture> handle) : base(handle, "UITexture")
        {
        }
    }
}