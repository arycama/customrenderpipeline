using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public struct SkyReflectionAmbientData : IRenderPassData
    {
        private readonly RTHandle reflectionProbe, skyCdf;
        private readonly BufferHandle ambientBuffer;
        private readonly RTHandle skyLuminance;
        private readonly RTHandle weightedDepth;
        private Vector2 skyLuminanceSize;
        private Vector2 cdfLookupSize;

        public SkyReflectionAmbientData(BufferHandle ambientBuffer, RTHandle reflectionProbe, RTHandle skyCdf, RTHandle skyLuminance, RTHandle weightedDepth, Vector2 skyLuminanceSize, Vector2 cdfLookupSize)
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
            pass.SetVector("_SkyCdfSize", new Vector2(skyCdf.Width, skyCdf.Height));
            pass.SetVector("SkyLuminanceScaleLimit", skyLuminance.ScaleLimit2D);
            pass.SetVector("SkyLuminanceSize", skyLuminanceSize);
            pass.SetVector("_SkyCdfSize", cdfLookupSize);
        }
    }
}
