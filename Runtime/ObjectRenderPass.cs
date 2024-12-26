using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace Arycama.CustomRenderPipeline
{
    public class ObjectRenderPass : GraphicsRenderPass
    {
        private RendererList rendererList;

        public void Initialize(string tag, ScriptableRenderContext context, CullingResults cullingResults, Camera camera, RenderQueueRange renderQueueRange, SortingCriteria sortingCriteria = SortingCriteria.None, PerObjectData perObjectData = PerObjectData.None, bool excludeMotionVectors = false)
        {
            var rendererListDesc = new RendererListDesc(new ShaderTagId(tag), cullingResults, camera)
            {
                renderQueueRange = renderQueueRange,
                sortingCriteria = sortingCriteria,
                excludeObjectMotionVectors = excludeMotionVectors,
                rendererConfiguration = perObjectData
            };

            rendererList = context.CreateRendererList(rendererListDesc);
        }

        public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            command.SetGlobalTexture(propertyName, texture);
            //postRender.Add(cmd => cmd.SetGlobalTexture(propertyName, BuiltinRenderTextureType.None));
        }

        public override void SetBuffer(string propertyName, BufferHandle buffer)
        {
            command.SetGlobalBuffer(propertyName, buffer.Resource);
            //postRender.Add(cmd => cmd.SetGlobalBuffer(propertyName, (GraphicsBuffer)null));
        }

        public override void SetBuffer(string propertyName, GraphicsBuffer buffer)
        {
            command.SetGlobalBuffer(propertyName, buffer);
            //postRender.Add(cmd => cmd.SetGlobalBuffer(propertyName, (GraphicsBuffer)null));
        }

        public override void SetVector(string propertyName, Vector4 value)
        {
            command.SetGlobalVector(propertyName, value);
            //postRender.Add(cmd => cmd.SetGlobalVector(propertyName, Vector4.zero));
        }

        public override void SetVectorArray(string propertyName, Vector4[] value)
        {
            command.SetGlobalVectorArray(propertyName, value);
        }

        public override void SetFloat(string propertyName, float value)
        {
            command.SetGlobalFloat(propertyName, value);
            //postRender.Add(cmd => cmd.SetGlobalFloat(propertyName, 0.0f));
        }

        public override void SetFloatArray(string propertyName, float[] value)
        {
            command.SetGlobalFloatArray(propertyName, value);
        }

        public override void SetInt(string propertyName, int value)
        {
            command.SetGlobalInt(propertyName, value);
            //postRender.Add(cmd => cmd.SetGlobalInt(propertyName, 0));
        }

        protected override void Execute()
        {
            command.DrawRendererList(rendererList);
        }

        public override void SetMatrix(string propertyName, Matrix4x4 value)
        {
            command.SetGlobalMatrix(propertyName, value);
        }

        public override void SetConstantBuffer(string propertyName, BufferHandle value)
        {
            command.SetGlobalConstantBuffer(value.Resource, propertyName, 0, value.Size);
        }

        public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
        {
            command.SetGlobalMatrixArray(propertyName, value);
        }
    }
}