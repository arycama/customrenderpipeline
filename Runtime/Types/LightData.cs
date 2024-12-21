using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct LightData
    {
        private readonly Vector3 position;
        private readonly float range;
        private readonly Vector3 color;
        private readonly uint lightType;
        private readonly Vector3 right;
        private readonly float angleScale;
        private readonly Vector3 up;
        private readonly float angleOffset;
        private readonly Vector3 forward;
        private readonly uint shadowIndex;
        private readonly Vector2 size;
        private readonly float depthRemapScale;
        private readonly float depthRemapOffset;

        public LightData(Vector3 positionWS, float range, Vector3 color, uint lightType, Vector3 right, float angleScale, Vector3 up, float angleOffset, Vector3 forward, uint shadowIndex, Vector2 size, float depthRemapScale, float depthRemapOffset) : this()
        {
            position = positionWS;
            this.range = range;
            this.color = color;
            this.lightType = lightType; // (uint)LightingUtils.GetLightType(light);
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