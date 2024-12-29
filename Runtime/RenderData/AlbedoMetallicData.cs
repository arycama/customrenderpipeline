using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class AlbedoMetallicData : RTHandleData
    {
        public AlbedoMetallicData(ResourceHandle<RenderTexture> handle) : base(handle, "_AlbedoMetallic")
        {
        }
    }
}