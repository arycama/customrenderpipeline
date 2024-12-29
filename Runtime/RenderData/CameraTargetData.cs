using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class CameraTargetData : RTHandleData
    {
        // TODO: Rename to CameraTarget?
        public CameraTargetData(ResourceHandle<RenderTexture> handle) : base(handle, "_Input")
        {
        }
    }
}