using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class FullscreenRenderPass : RenderPass
    {
        private MaterialPropertyBlock propertyBlock;
        public Material Material { get; set; }
        public int PassIndex { get; set; }

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

        public override void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer)
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
    }
}