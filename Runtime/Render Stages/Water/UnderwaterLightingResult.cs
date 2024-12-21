using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public readonly struct UnderwaterLightingResult : IRenderPassData
    {
        private readonly RTHandle underwaterLighting;

        public UnderwaterLightingResult(RTHandle waterNormalFoam)
        {
            underwaterLighting = waterNormalFoam ?? throw new ArgumentNullException(nameof(waterNormalFoam));
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_UnderwaterResult", underwaterLighting);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector("_UnderwaterResultScaleLimit", underwaterLighting.ScaleLimit2D);
        }
    }
}