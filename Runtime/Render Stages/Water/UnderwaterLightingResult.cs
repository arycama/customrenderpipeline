using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public struct UnderwaterLightingResult : IRenderPassData
    {
        private RTHandle underwaterLighting;

        public UnderwaterLightingResult(RTHandle waterNormalFoam)
        {
            this.underwaterLighting = waterNormalFoam ?? throw new ArgumentNullException(nameof(waterNormalFoam));
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_UnderwaterResult", underwaterLighting);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(command, "_UnderwaterResultScaleLimit", underwaterLighting.ScaleLimit2D);
        }
    }
}