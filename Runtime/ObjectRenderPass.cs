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

        public override void SetTexture(CommandBuffer command, string propertyName, Texture texture)
        {
            command.SetGlobalTexture(propertyName, texture);
            //postRender.Add(cmd => cmd.SetGlobalTexture(propertyName, BuiltinRenderTextureType.None));
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer)
        {
            command.SetGlobalBuffer(propertyName, buffer);
            //postRender.Add(cmd => cmd.SetGlobalBuffer(propertyName, (GraphicsBuffer)null));
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            command.SetGlobalVector(propertyName, value);
            //postRender.Add(cmd => cmd.SetGlobalVector(propertyName, Vector4.zero));
        }

        public override void SetVectorArray(CommandBuffer command, string propertyName, Vector4[] value)
        {
            command.SetGlobalVectorArray(propertyName, value);
        }

        public override void SetFloat(CommandBuffer command, string propertyName, float value)
        {
            command.SetGlobalFloat(propertyName, value);
            //postRender.Add(cmd => cmd.SetGlobalFloat(propertyName, 0.0f));
        }

        public override void SetFloatArray(CommandBuffer command, string propertyName, float[] value)
        {
            command.SetGlobalFloatArray(propertyName, value);
        }

        public override void SetInt(CommandBuffer command, string propertyName, int value)
        {
            command.SetGlobalInt(propertyName, value);
            //postRender.Add(cmd => cmd.SetGlobalInt(propertyName, 0));
        }

        protected override void Execute(CommandBuffer command)
        {
            command.DrawRendererList(rendererList);
        }

        public override void SetMatrix(CommandBuffer command, string propertyName, Matrix4x4 value)
        {
            command.SetGlobalMatrix(propertyName, value);
        }

        public override void SetConstantBuffer(CommandBuffer command, string propertyName, BufferHandle value)
        {
            command.SetGlobalConstantBuffer(value, propertyName, 0, value.Size);
        }

        public override void SetMatrixArray(CommandBuffer command, string propertyName, Matrix4x4[] value)
        {
            command.SetGlobalMatrixArray(propertyName, value);
        }
    }
}