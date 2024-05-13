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

        public void SetInputs(RenderPass pass)
        {
            // TODO: RTAS input handling
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}