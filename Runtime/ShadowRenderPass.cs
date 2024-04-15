using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ShadowRenderPass : RenderPass
    {
        private RendererList rendererList;
        private float bias, slopeBias;
        private bool zClip;

        public void WriteTexture(RTHandle texture)
        {
            RenderGraph.SetRTHandleWrite(texture, Index);
        }

        public void Initialize(ScriptableRenderContext context, CullingResults cullingResults, int lightIndex, BatchCullingProjectionType projectionType, ShadowSplitData shadowSplitData, float bias, float slopeBias, bool zClip)
        {
            this.bias = bias;
            this.slopeBias = slopeBias;
            this.zClip = zClip;

            var shadowDrawingSettings = new ShadowDrawingSettings(cullingResults, lightIndex, projectionType)
            {
                splitData = shadowSplitData
            };

            rendererList = context.CreateShadowRendererList(ref shadowDrawingSettings);
        }

        public override void SetTexture(CommandBuffer command, int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            command.SetGlobalTexture(propertyName, texture);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, BufferHandle buffer)
        {
            command.SetGlobalBuffer(propertyName, buffer);
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            command.SetGlobalVector(propertyName, value);
        }

        public override void SetVectorArray(CommandBuffer command, string propertyName, Vector4[] value)
        {
            command.SetGlobalVectorArray(propertyName, value);
        }

        public override void SetFloat(CommandBuffer command, string propertyName, float value)
        {
            command.SetGlobalFloat(propertyName, value);
        }

        public override void SetFloatArray(CommandBuffer command, string propertyName, float[] value)
        {
            command.SetGlobalFloatArray(propertyName, value);
        }

        public override void SetInt(CommandBuffer command, string propertyName, int value)
        {
            command.SetGlobalInt(propertyName, value);
        }

        protected override void Execute(CommandBuffer command)
        {
            command.SetGlobalDepthBias(bias, slopeBias);
            command.SetGlobalFloat("_ZClip", zClip ? 1.0f : 0.0f);
            command.DrawRendererList(rendererList);
            command.SetGlobalDepthBias(0.0f, 0.0f);
            command.SetGlobalFloat("_ZClip", 1.0f);
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

        protected override void SetupTargets(CommandBuffer command)
        {
        }
    }
}