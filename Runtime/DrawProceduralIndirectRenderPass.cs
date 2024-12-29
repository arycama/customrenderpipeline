using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class DrawProceduralIndirectRenderPass : GraphicsRenderPass
    {
        public readonly MaterialPropertyBlock propertyBlock;
        private Material material;
        private int passIndex;
        private BufferHandle indexBuffer;
        private BufferHandle indirectArgsBuffer;
        private MeshTopology topology;
        private float depthBias, slopeDepthBias;
        private bool zClip;

        public string Keyword { get; set; }

        public DrawProceduralIndirectRenderPass()
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        public override string ToString()
        {
            return $"{Name} {material} {passIndex}";
        }

        public void Initialize(Material material, BufferHandle indexBuffer, BufferHandle indirectArgsBuffer, MeshTopology topology = MeshTopology.Triangles, int passIndex = 0, string keyword = null, float depthBias = 0.0f, float slopeDepthBias = 0.0f, bool zClip = true)
        {
            this.material = material;
            this.passIndex = passIndex;
            Keyword = keyword;
            this.indexBuffer = indexBuffer;
            this.indirectArgsBuffer = indirectArgsBuffer;
            this.topology = topology;
            this.depthBias = depthBias;
            this.slopeDepthBias = slopeDepthBias;
            this.zClip = zClip;

            ReadBuffer("", indexBuffer);
            ReadBuffer("", indirectArgsBuffer);
        }

        public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            propertyBlock.SetTexture(propertyName, texture);
        }

        public override void SetBuffer(string propertyName, BufferHandle buffer)
        {
            propertyBlock.SetBuffer(propertyName, buffer.Resource);
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
            if (!string.IsNullOrEmpty(Keyword))
            {
                command.EnableShaderKeyword(Keyword);
            }

            if (depthBias != 0.0f || slopeDepthBias != 0.0f)
                command.SetGlobalDepthBias(depthBias, slopeDepthBias);

            command.SetGlobalFloat("_ZClip", zClip ? 1.0f : 0.0f);
            command.DrawProceduralIndirect(indexBuffer.Resource, Matrix4x4.identity, material, passIndex, topology, indirectArgsBuffer.Resource, 0, propertyBlock);
            command.SetGlobalFloat("_ZClip", 1.0f);

            if (depthBias != 0.0f || slopeDepthBias != 0.0f)
                command.SetGlobalDepthBias(0.0f, 0.0f);

            if (!string.IsNullOrEmpty(Keyword))
            {
                command.DisableShaderKeyword(Keyword);
                Keyword = null;
            }

            material = null;
            passIndex = 0;
            propertyBlock.Clear();
            zClip = true;
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
    }
}