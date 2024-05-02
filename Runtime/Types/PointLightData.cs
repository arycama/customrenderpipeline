using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct PointLightData
    {
        public Vector3 Position { get; }
        public float SqRange { get; }

        public float SqRcpRange { get; }
        public uint ShadowIndexVisibleFaces { get; }
        public float DepthRemapScale { get; }
        public float DepthRemapOffset { get; }

        public Vector3 Color { get; }
        public float Padding { get; }

        public PointLightData(Vector3 position, float range, Vector3 color, int shadowIndex, int visibleFaces, float near, float far)
        {
            Position = position;
            SqRange = range * range;

            SqRcpRange = 1.0f / (range * range);
            ShadowIndexVisibleFaces = (uint)(visibleFaces | (shadowIndex << 8));
            DepthRemapScale = -(near * far) / (near - far);
            DepthRemapOffset = 1 + far / (near - far);

            Color = color;
            Padding = 0.0f;
        }
    }
}