using System;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public readonly struct OceanFftResult : IRenderPassData
    {
        private readonly RTHandle oceanDisplacement;
        private readonly RTHandle oceanDisplacementHistory;
        private readonly RTHandle oceanNormalFoamSmoothness;
        private readonly RTHandle lengthToRoughness;
        private readonly BufferHandle oceanBuffer;

        public OceanFftResult(RTHandle oceanDisplacement, RTHandle oceanDisplacementHistory, RTHandle oceanNormalFoamSmoothness, RTHandle lengthToRoughness, BufferHandle oceanBuffer)
        {
            this.oceanDisplacement = oceanDisplacement ?? throw new ArgumentNullException(nameof(oceanDisplacement));
            this.oceanDisplacementHistory = oceanDisplacementHistory ?? throw new ArgumentNullException(nameof(oceanDisplacementHistory));
            this.oceanNormalFoamSmoothness = oceanNormalFoamSmoothness ?? throw new ArgumentNullException(nameof(oceanNormalFoamSmoothness));
            this.lengthToRoughness = lengthToRoughness;
            this.oceanBuffer = oceanBuffer;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("OceanDisplacement", oceanDisplacement);
            pass.ReadTexture("OceanDisplacementHistory", oceanDisplacementHistory);
            pass.ReadTexture("OceanNormalFoamSmoothness", oceanNormalFoamSmoothness);
            pass.ReadTexture("_LengthToRoughness", lengthToRoughness);
            pass.ReadBuffer("OceanData", oceanBuffer);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}