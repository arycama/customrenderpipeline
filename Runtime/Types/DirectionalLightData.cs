using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public readonly struct DirectionalLightData
    {
        public Vector3 Color { get; }
        public uint ShadowIndex { get; }
        public Vector3 Direction { get; }
        public uint CascadeCount { get; }
        public Matrix3x4 WorldToLight { get; }

        public DirectionalLightData(Vector3 color, uint shadowIndex, Vector3 direction, uint cascadeCount, Matrix3x4 worldToLight)
        {
            Color = color;
            ShadowIndex = shadowIndex;
            Direction = direction;
            CascadeCount = cascadeCount;
            WorldToLight = worldToLight;
        }
    }
}