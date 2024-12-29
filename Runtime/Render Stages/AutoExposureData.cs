using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct AutoExposureData : IRenderPassData
    {
        public BufferHandle exposureBuffer { get; }
        public bool IsFirst { get; }

        public AutoExposureData(BufferHandle exposureBuffer, bool isFirst)
        {
            this.exposureBuffer = exposureBuffer;
            IsFirst = isFirst;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("Exposure", exposureBuffer);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}