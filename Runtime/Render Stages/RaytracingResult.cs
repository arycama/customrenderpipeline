using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public struct RaytracingResult : IRenderPassData
    {
        public RayTracingAccelerationStructure Rtas { get; private set; }

        public RaytracingResult(RayTracingAccelerationStructure rtas)
        {
            Rtas = rtas ?? throw new ArgumentNullException(nameof(rtas));
        }

        public readonly void SetInputs(RenderPass pass)
        {
            // TODO: RTAS input handling
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}