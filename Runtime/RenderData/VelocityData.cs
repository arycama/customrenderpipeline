using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class VelocityData : RTHandleData
    {
        public VelocityData(ResourceHandle<RenderTexture> handle) : base(handle, "Velocity")
        {
        }
    }
}