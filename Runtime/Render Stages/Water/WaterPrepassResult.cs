using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public struct WaterPrepassResult : IRenderPassData
    {
        private RTHandle waterNormalFoam, waterTriangleNormal;
        private Vector3 albedo, extinction;

        public WaterPrepassResult(RTHandle waterNormalFoam, RTHandle waterTriangleNormal, Vector3 albedo, Vector3 extinction)
        {
            this.waterNormalFoam = waterNormalFoam ?? throw new ArgumentNullException(nameof(waterNormalFoam));
            this.waterTriangleNormal = waterTriangleNormal ?? throw new ArgumentNullException(nameof(waterTriangleNormal));
            this.albedo = albedo;
            this.extinction = extinction;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_WaterNormalFoam", waterNormalFoam);
            pass.ReadTexture("_WaterTriangleNormal", waterTriangleNormal);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(command, "_WaterAlbedo", albedo);
            pass.SetVector(command, "_WaterExtinction", extinction);
        }
    }
}