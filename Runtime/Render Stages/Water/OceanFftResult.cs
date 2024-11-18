using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public struct OceanFftResult : IRenderPassData
    {
        public RTHandle OceanDisplacement { get; }
        public RTHandle OceanDisplacementHistory { get; }
        public RTHandle OceanNormalFoamSmoothness { get; }
        private RTHandle lengthToRoughness;

        public OceanFftResult(RTHandle oceanDisplacement, RTHandle oceanDisplacementHistory, RTHandle oceanNormalFoamSmoothness, RTHandle lengthToRoughness)
        {
            OceanDisplacement = oceanDisplacement ?? throw new ArgumentNullException(nameof(oceanDisplacement));
            OceanDisplacementHistory = oceanDisplacementHistory ?? throw new ArgumentNullException(nameof(oceanDisplacementHistory));
            OceanNormalFoamSmoothness = oceanNormalFoamSmoothness ?? throw new ArgumentNullException(nameof(oceanNormalFoamSmoothness));
            this.lengthToRoughness = lengthToRoughness;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("OceanDisplacement", OceanDisplacement);
            pass.ReadTexture("OceanDisplacementHistory", OceanDisplacementHistory);
            pass.ReadTexture("OceanNormalFoamSmoothness", OceanNormalFoamSmoothness);
            pass.ReadTexture("_LengthToRoughness", lengthToRoughness);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}