using UnityEngine;
using UnityEngine.Rendering;
using Unmath;

namespace CustomRenderPipeline
{
    public struct SkyReflectionAmbientData : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> skyCdf;
        private readonly ResourceHandle<RenderTexture> skyLuminance;
        private readonly ResourceHandle<RenderTexture> weightedDepth;

        public SkyReflectionAmbientData(ResourceHandle<RenderTexture> skyCdf, ResourceHandle<RenderTexture> skyLuminance, ResourceHandle<RenderTexture> weightedDepth)
        {
            this.skyCdf = skyCdf;
            this.skyLuminance = skyLuminance;
            this.weightedDepth = weightedDepth;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("SkyCdf", skyCdf);
            pass.ReadTexture("SkyLuminance", skyLuminance);
            pass.ReadTexture("AtmosphereDepth", weightedDepth);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}