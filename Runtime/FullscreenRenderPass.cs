using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class FullscreenRenderPass : GraphicsRenderPass
    {
        private readonly MaterialPropertyBlock propertyBlock;
        public Material Material { get; private set; }
        public int PassIndex { get; private set; }

        public FullscreenRenderPass()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        public void Initialize(Material material, int passIndex = 0)
        {
            Material = material;
            PassIndex = passIndex;
        }

        public override void SetTexture(CommandBuffer command, string propertyName, Texture texture)
        {
            if(texture == null)
            {
                Debug.Log(propertyName);
            }

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
            command.DrawProcedural(Matrix4x4.identity, Material, PassIndex, MeshTopology.Triangles, 3, 1, propertyBlock);
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