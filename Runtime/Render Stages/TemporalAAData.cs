using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class TemporalAAData : ConstantBufferData
    {
        public TemporalAAData(ResourceHandle<GraphicsBuffer> buffer) : base(buffer, "TemporalProperties")
        {
        }
    }
}