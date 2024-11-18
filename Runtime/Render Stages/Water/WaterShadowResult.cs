using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public readonly struct WaterShadowResult : IRenderPassData
    {
        private readonly RTHandle waterShadowTexture;
        private readonly Matrix4x4 waterShadowMatrix;
        private readonly float waterShadowNear, waterShadowFar;
        private readonly Vector3 waterShadowExtinction;

        public WaterShadowResult(RTHandle waterShadowTexture, Matrix4x4 waterShadowMatrix, float waterShadowNear, float waterShadowFar, Vector3 waterShadowExtinction)
        {
            this.waterShadowTexture = waterShadowTexture ?? throw new ArgumentNullException(nameof(waterShadowTexture));
            this.waterShadowMatrix = waterShadowMatrix;
            this.waterShadowNear = waterShadowNear;
            this.waterShadowFar = waterShadowFar;
            this.waterShadowExtinction = waterShadowExtinction;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("_WaterShadows", waterShadowTexture);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetMatrix(command, "_WaterShadowMatrix1", waterShadowMatrix);
            pass.SetFloat(command, "_WaterShadowNear", waterShadowNear);
            pass.SetFloat(command, "_WaterShadowFar", waterShadowFar);
            pass.SetVector(command, "_WaterShadowExtinction", waterShadowExtinction);
        }
    }
}