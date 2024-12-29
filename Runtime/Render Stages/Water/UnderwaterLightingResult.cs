using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public readonly struct UnderwaterLightingResult : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> underwaterLighting;

        public UnderwaterLightingResult(ResourceHandle<RenderTexture> waterNormalFoam)
        {
            underwaterLighting = waterNormalFoam;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_UnderwaterResult", underwaterLighting);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector("_UnderwaterResultScaleLimit", pass.GetScaleLimit2D(underwaterLighting));
        }
    }
}