using Unmath;

namespace CustomRenderPipeline
{
    public readonly struct LightData
    {
        public readonly Float3 position;
        public readonly float rangeSquaredRcp;
        public readonly Float3 forward;
        public readonly float angleScale;
        public readonly Float3 color;
        public readonly float angleOffset;
        public readonly Float4 cullingSphere;

        // TODO: Can we simplify any of this
        public readonly Float3 right;
        public readonly uint lightType;
        public readonly Float3 up;
        public readonly uint shadowIndex;
        public readonly Float2 size;
        public readonly float depthRemapScale;
        public readonly float depthRemapOffset;

        public LightData(Float3 positionWS, float rangeSquaredRcp, Float3 forward, float angleScale, Float3 color, float angleOffset, Float4 cullingSphere, Float3 right, uint lightType, Float3 up, uint shadowIndex, Float2 size, float depthRemapScale, float depthRemapOffset)
        {
            position = positionWS;
            this.rangeSquaredRcp = rangeSquaredRcp;
            this.color = color;
            this.lightType = lightType;
            this.right = right;
            this.angleScale = angleScale;
            this.up = up;
            this.angleOffset = angleOffset;
            this.cullingSphere = cullingSphere;
            this.forward = forward;
            this.shadowIndex = shadowIndex;
            this.size = size;
            this.depthRemapScale = depthRemapScale; // -(near * far) / (near - far);
            this.depthRemapOffset = depthRemapOffset; // 1 + far / (near - far);
        }
    }
}