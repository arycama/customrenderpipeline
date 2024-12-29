using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public struct WaterPrepassResult : IRenderPassData
    {
        private readonly RTHandle waterNormalFoam, waterTriangleNormal;
        private Vector3 albedo, extinction;

        public WaterPrepassResult(RTHandle waterNormalFoam, RTHandle waterTriangleNormal, Vector3 albedo, Vector3 extinction)
        {
            this.waterNormalFoam = waterNormalFoam;
            this.waterTriangleNormal = waterTriangleNormal;
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
            pass.SetVector("_WaterAlbedo", albedo);
            pass.SetVector("_WaterExtinction", extinction);
        }
    }
}