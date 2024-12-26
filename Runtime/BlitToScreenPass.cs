using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class BlitToScreenPass : RenderPass
    {
        private readonly MaterialPropertyBlock propertyBlock;
        private Material material;
        private int passIndex;

        public override string ToString()
        {
            return $"{Name} {material} {passIndex}";
        }

        public BlitToScreenPass()
        {
            propertyBlock = new();
        }

        public void Initialize(Material material, int passIndex = 0)
        {
            this.material = material;
            this.passIndex = passIndex;
        }

        public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
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

        public override void SetBuffer(string propertyName, BufferHandle buffer)
        {
            propertyBlock.SetBuffer(propertyName, buffer.Resource);
        }

        public override void SetBuffer(string propertyName, GraphicsBuffer buffer)
        {
            propertyBlock.SetBuffer(propertyName, buffer);
        }

        public override void SetVector(string propertyName, Vector4 value)
        {
            propertyBlock.SetVector(propertyName, value);
        }

        public override void SetVectorArray(string propertyName, Vector4[] value)
        {
            propertyBlock.SetVectorArray(propertyName, value);
        }

        public override void SetFloat(string propertyName, float value)
        {
            propertyBlock.SetFloat(propertyName, value);
        }

        public override void SetFloatArray(string propertyName, float[] value)
        {
            propertyBlock.SetFloatArray(propertyName, value);
        }

        public override void SetInt(string propertyName, int value)
        {
            propertyBlock.SetInt(propertyName, value);
        }

        protected override void Execute()
        {
            command.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1, propertyBlock);

            material = null;
            passIndex = 0;
        }

        public override void SetMatrix(string propertyName, Matrix4x4 value)
        {
            propertyBlock.SetMatrix(propertyName, value);
        }

        public override void SetConstantBuffer(string propertyName, BufferHandle value)
        {
            propertyBlock.SetConstantBuffer(propertyName, value.Resource, 0, value.Size);
        }

        public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
        {
            propertyBlock.SetMatrixArray(propertyName, value);
        }

        protected override void SetupTargets()
        {
            command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        }
    }
}