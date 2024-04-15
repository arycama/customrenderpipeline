using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class FullscreenRenderPass : GraphicsRenderPass
    {
        private static Vector3[] frustumCorners = new Vector3[4];

        public readonly MaterialPropertyBlock propertyBlock;
        private Material material;
        private int passIndex;
        private int primitiveCount;

        private Vector4[] corners = new Vector4[3];

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
                camera.CalculateFrustumCorners(new Rect(0.0f, 0.0f, 1.0f, 1.0f), 1.0f, camera.stereoActiveEye, frustumCorners);

                var topLeft = camera.transform.TransformVector(frustumCorners[0]);
                var bottomLeft = camera.transform.TransformVector(frustumCorners[1]);
                var bottomRight = camera.transform.TransformVector(frustumCorners[2]);

                corners[0] = bottomLeft;
                corners[1] = bottomLeft + (bottomRight - bottomLeft) * 2.0f;
                corners[2] = bottomLeft + (topLeft - bottomLeft) * 2.0f;
            }
        }

        public override void SetTexture(CommandBuffer command, int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            switch(subElement)
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

            if(!string.IsNullOrEmpty(Keyword))
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