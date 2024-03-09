using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class FullscreenRenderPass : GraphicsRenderPass
    {
        private readonly MaterialPropertyBlock propertyBlock;
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
            this.Keyword = keyword;
        }

        public override void SetTexture(CommandBuffer command, string propertyName, Texture texture)
        {
            propertyBlock.SetTexture(propertyName, texture);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer)
        {
            propertyBlock.SetBuffer(propertyName, buffer);
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            propertyBlock.SetVector(propertyName, value);
        }

        public override void SetFloat(CommandBuffer command, string propertyName, float value)
        {
            propertyBlock.SetFloat(propertyName, value);
        }

        public override void SetInt(CommandBuffer command, string propertyName, int value)
        {
            propertyBlock.SetInt(propertyName, value);
        }

        protected override void Execute(CommandBuffer command)
        {
            LocalKeyword keyword = default;
            if (!string.IsNullOrEmpty(Keyword))
            {
                //keyword = new LocalKeyword(material.shader, Keyword);
                command.EnableShaderKeyword(Keyword);
            }

            command.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3 * primitiveCount, 1, propertyBlock);

            if(!string.IsNullOrEmpty(Keyword))
            {
                command.DisableShaderKeyword(Keyword);
                Keyword = null;
            }

            material = null;
            passIndex = 0;
            primitiveCount = 1;
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