using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class TemporalAASetupData : IRenderPassData
    {
        public Vector2 Jitter { get; }

        public TemporalAASetupData(Vector2 jitter)
        {
            Jitter = jitter;
        }

        void IRenderPassData.SetInputs(RenderPass pass)
        {
        }

        void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}