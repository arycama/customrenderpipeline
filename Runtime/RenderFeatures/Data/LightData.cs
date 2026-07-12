using Unmath;

namespace CustomRenderPipeline
{
    public readonly struct LightData
    {
        private readonly Float3 position;
        private readonly float rangeSquaredRcp;
        private readonly Float3 forward;
        private readonly float angleScale;
        private readonly Float3 color;
        private readonly float angleOffset;

        // TODO: Can we simplify any of this
        private readonly Float3 right;
        private readonly uint lightType;
        private readonly Float3 up;
        private readonly uint shadowIndex;
        private readonly Float2 size;
        private readonly float depthRemapScale;
        private readonly float depthRemapOffset;

        public LightData(Float3 positionWS, float rangeSquaredRcp, Float3 forward, float angleScale, Float3 color, float angleOffset, Float3 right, uint lightType, Float3 up, uint shadowIndex, Float2 size, float depthRemapScale, float depthRemapOffset)
        {
            position = positionWS;
            this.rangeSquaredRcp = rangeSquaredRcp;
            this.color = color;
            this.lightType = lightType;
            this.right = right;
            this.angleScale = angleScale;
            this.up = up;
            this.angleOffset = angleOffset;
            this.forward = forward;
            this.shadowIndex = shadowIndex;
            this.size = size;
            this.depthRemapScale = depthRemapScale; // -(near * far) / (near - far);
            this.depthRemapOffset = depthRemapOffset; // 1 + far / (near - far);
        }
    }
}