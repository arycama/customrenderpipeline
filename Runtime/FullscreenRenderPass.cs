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

        public string Keyword { get; set; }

        public FullscreenRenderPass()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        public override string ToString()
        {
            return $"{Name} {material} {passIndex}";
        }

        public void Initialize(Material material, int passIndex = 0, int primitiveCount = 1, string keyword = null)
        {
            this.material = material;
            this.passIndex = passIndex;
            this.primitiveCount = primitiveCount;
            Keyword = keyword;
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

        public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
        {
            propertyBlock.SetBuffer(propertyName, GetBuffer(buffer));
        }

        public override void SetVector(int propertyName, Vector4 value)
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
            if (!string.IsNullOrEmpty(Keyword))
            {
                //keyword = new LocalKeyword(material.shader, Keyword);
                Command.EnableShaderKeyword(Keyword);
            }

            Command.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3 * primitiveCount, 1, propertyBlock);

            if (!string.IsNullOrEmpty(Keyword))
            {
                Command.DisableShaderKeyword(Keyword);
                Keyword = null;
            }

            material = null;
            passIndex = 0;
            primitiveCount = 1;
            propertyBlock.Clear();
        }

        public override void SetMatrix(string propertyName, Matrix4x4 value)
        {
            propertyBlock.SetMatrix(propertyName, value);
        }

        public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value)
        {
            var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
            var size = descriptor.Count * descriptor.Stride;
            propertyBlock.SetConstantBuffer(propertyName, GetBuffer(value), 0, size);
        }

        public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
        {
            propertyBlock.SetMatrixArray(propertyName, value);
        }
    }
}