using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public readonly struct WaterShadowCullResult : IRenderPassData
    {
        public BufferHandle IndirectArgsBuffer { get; }
        public BufferHandle PatchDataBuffer { get; }
        public float Near { get; }
        public float Far { get; }
        public Matrix4x4 WorldToClip { get; }
        public Matrix4x4 ShadowMatrix { get; }
        public CullingPlanes CullingPlanes { get; }

        public WaterShadowCullResult(BufferHandle indirectArgsBuffer, BufferHandle patchDataBuffer, float near, float far, Matrix4x4 worldToClip, Matrix4x4 shadowMatrix, CullingPlanes cullingPlanes)
        {
            IndirectArgsBuffer = indirectArgsBuffer ?? throw new ArgumentNullException(nameof(indirectArgsBuffer));
            PatchDataBuffer = patchDataBuffer ?? throw new ArgumentNullException(nameof(patchDataBuffer));
            Near = near;
            Far = far;
            WorldToClip = worldToClip;
            ShadowMatrix = shadowMatrix;
            CullingPlanes = cullingPlanes;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadBuffer("_PatchData", PatchDataBuffer);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
        }
    }
}