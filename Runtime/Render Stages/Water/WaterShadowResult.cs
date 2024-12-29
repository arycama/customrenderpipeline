using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public readonly struct WaterShadowResult : IRenderPassData
    {
        private readonly ResourceHandle<RenderTexture> waterShadowTexture, waterIlluminance;
        private readonly Matrix4x4 waterShadowMatrix;
        private readonly float waterShadowNear, waterShadowFar;
        private readonly Vector3 waterShadowExtinction;

        public WaterShadowResult(ResourceHandle<RenderTexture> waterShadowTexture, Matrix4x4 waterShadowMatrix, float waterShadowNear, float waterShadowFar, Vector3 waterShadowExtinction, ResourceHandle<RenderTexture> waterIlluminance)
        {
            this.waterShadowTexture = waterShadowTexture;
            this.waterShadowMatrix = waterShadowMatrix;
            this.waterShadowNear = waterShadowNear;
            this.waterShadowFar = waterShadowFar;
            this.waterShadowExtinction = waterShadowExtinction;
            this.waterIlluminance = waterIlluminance;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_WaterShadows", waterShadowTexture);
            pass.ReadTexture("WaterIlluminance", waterIlluminance);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetMatrix("_WaterShadowMatrix1", waterShadowMatrix);
            pass.SetFloat("_WaterShadowNear", waterShadowNear);
            pass.SetFloat("_WaterShadowFar", waterShadowFar);
            pass.SetVector("_WaterShadowExtinction", waterShadowExtinction);
        }
    }
}