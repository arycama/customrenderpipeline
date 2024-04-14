using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class GlobalRenderPass : RenderPass
    {
        public void WriteTexture(RTHandle texture)
        {
            RenderGraph.SetRTHandleWrite(texture, Index);
        }

        public override void SetTexture(CommandBuffer command, string propertyName, Texture texture, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            // Should also clean up on post render.. but 
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
            // Does nothing (Eventually could do a command.setglobalbuffer or something?)
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