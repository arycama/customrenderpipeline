using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public struct SkyReflectionAmbientData : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> reflectionProbe, skyCdf;
        private readonly ResourceHandle<GraphicsBuffer> ambientBuffer;
        private readonly ResourceHandle<RenderTexture> skyLuminance;
        private readonly ResourceHandle<RenderTexture> weightedDepth;
        private Vector2 skyLuminanceSize;
        private Vector2 cdfLookupSize;

        public SkyReflectionAmbientData(ResourceHandle<GraphicsBuffer> ambientBuffer, ResourceHandle<RenderTexture> reflectionProbe, ResourceHandle<RenderTexture> skyCdf, ResourceHandle<RenderTexture> skyLuminance, ResourceHandle<RenderTexture> weightedDepth, Vector2 skyLuminanceSize, Vector2 cdfLookupSize)
        {
            this.ambientBuffer = ambientBuffer;
            this.reflectionProbe = reflectionProbe;
            this.skyCdf = skyCdf;
            this.skyLuminance = skyLuminance;
            this.weightedDepth = weightedDepth;
            this.skyLuminanceSize = skyLuminanceSize;
            this.cdfLookupSize = cdfLookupSize;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_SkyReflection", reflectionProbe);
            pass.ReadTexture("_SkyCdf", skyCdf);
            pass.ReadBuffer("AmbientSh", ambientBuffer);
            pass.ReadTexture("SkyLuminance", skyLuminance);
            pass.ReadTexture("_SkyCdf", skyCdf);
            pass.ReadTexture("_AtmosphereDepth", weightedDepth);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector("SkyLuminanceScaleLimit", pass.GetScaleLimit2D(skyLuminance));
            pass.SetVector("SkyLuminanceSize", skyLuminanceSize);
            pass.SetVector("_SkyCdfSize", cdfLookupSize);
        }
    }
}
