using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class FullscreenRenderPass : GraphicsRenderPass
    {
        // Todo: This should be private, only kept for some niche cases that will be fixed
        public readonly MaterialPropertyBlock propertyBlock;

        private Material material;
        private int passIndex;
        private int primitiveCount;

        private readonly Vector4[] corners = new Vector4[3];

        public string Keyword { get; set; }

        public FullscreenRenderPass()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        public override string ToString()
        {
            return $"{Name} {material} {passIndex}";
        }

        public void Initialize(Material material, int passIndex = 0, int primitiveCount = 1, string keyword = null, Camera camera = null)
        {
            this.material = material;
            this.passIndex = passIndex;
            this.primitiveCount = primitiveCount;
            this.Keyword = keyword;

            // Camera frustum rays
            if (camera != null)
            {
                var aspect = camera.aspect;
                var tanHalfFov = MathF.Tan(0.5f * Mathf.Deg2Rad * (float)camera.fieldOfView);

                var temporalData = RenderGraph.ResourceMap.GetRenderPassData<TemporalAA.TemporalAAData>(RenderGraph.FrameIndex);
                var jitterX = 2.0f * temporalData.Jitter.z * tanHalfFov;
                var jitterY = 2.0f * temporalData.Jitter.w * tanHalfFov;

                var corner0 = new Vector3((-tanHalfFov + jitterX) * aspect, tanHalfFov + jitterY, 1.0f);

                Vector3 corner1;
                corner1.x = aspect * (3.0f * tanHalfFov + jitterX);
                corner1.y = tanHalfFov + jitterY;
                corner1.z = 1.0f;

                Vector3 corner2;
                corner2.x = (-tanHalfFov + jitterX) * aspect;
                corner2.y = -3.0f * tanHalfFov + jitterY;
                corner2.z = 1.0f;

                var rotation = camera.transform.rotation;
                corners[0] = rotation * corner0;
                corners[1] = rotation * corner1;
                corners[2] = rotation * corner2;
            }
        }

        public override void SetTexture(CommandBuffer command, int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            switch (subElement)
            {
                case RenderTextureSubElement.Depth:
                    propertyBlock.SetTexture(propertyName, (RenderTexture)texture, RenderTextureSubElement.Depth);
                    break;
                case RenderTextureSubElement.Stencil:
                    propertyBlock.SetTexture(propertyName, (RenderTexture)texture, RenderTextureSubElement.Stencil);
                    break;
                default:
                    propertyBlock.SetTexture(propertyName, texture);
                    break;
            }
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer)
        {
            propertyBlock.SetBuffer(propertyName, buffer);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer)
        {
            propertyBlock.SetBuffer(propertyName, buffer);
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            propertyBlock.SetVector(propertyName, value);
        }

        public override void SetVectorArray(CommandBuffer command, string propertyName, Vector4[] value)
        {
            propertyBlock.SetVectorArray(propertyName, value);
        }

        public override void SetFloat(CommandBuffer command, string propertyName, float value)
        {
            propertyBlock.SetFloat(propertyName, value);
        }

        public override void SetFloatArray(CommandBuffer command, string propertyName, float[] value)
        {
            propertyBlock.SetFloatArray(propertyName, value);
        }

        public override void SetInt(CommandBuffer command, string propertyName, int value)
        {
            propertyBlock.SetInt(propertyName, value);
        }

        protected override void Execute(CommandBuffer command)
        {
            if (!string.IsNullOrEmpty(Keyword))
            {
                //keyword = new LocalKeyword(material.shader, Keyword);
                command.EnableShaderKeyword(Keyword);
            }

            propertyBlock.SetVectorArray("_FrustumCorners", corners);

            command.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3 * primitiveCount, 1, propertyBlock);

            if (!string.IsNullOrEmpty(Keyword))
            {
                command.DisableShaderKeyword(Keyword);
                Keyword = null;
            }

            material = null;
            passIndex = 0;
            primitiveCount = 1;
            propertyBlock.Clear();
        }

        public override void SetMatrix(CommandBuffer command, string propertyName, Matrix4x4 value)
        {
            propertyBlock.SetMatrix(propertyName, value);
        }

        public override void SetConstantBuffer(CommandBuffer command, string propertyName, BufferHandle value)
        {
            propertyBlock.SetConstantBuffer(propertyName, value, 0, value.Size);
        }

        public override void SetMatrixArray(CommandBuffer command, string propertyName, Matrix4x4[] value)
        {
            propertyBlock.SetMatrixArray(propertyName, value);
        }
    }
}