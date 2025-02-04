using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class CameraStencilData : RTHandleData
    {
        public CameraStencilData(ResourceHandle<RenderTexture> handle) : base(handle, "_Stencil", subElement: RenderTextureSubElement.Stencil)
        {
        }
    }
}