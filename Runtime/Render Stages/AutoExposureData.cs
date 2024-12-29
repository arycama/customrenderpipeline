using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct AutoExposureData : IRenderPassData
    {
        public ResourceHandle<GraphicsBuffer> ExposureBuffer { get; }
        public bool IsFirst { get; }

        public AutoExposureData(ResourceHandle<GraphicsBuffer> exposureBuffer, bool isFirst)
        {
            this.ExposureBuffer = exposureBuffer;
            IsFirst = isFirst;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("Exposure", ExposureBuffer);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}