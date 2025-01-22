using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class GlobalRenderPass : RenderPass
    {
        public void WriteTexture(ResourceHandle<RenderTexture> rtHandle)
        {
            RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);
        }

        public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            // Should also clean up on post render.. but 
            Command.SetGlobalTexture(propertyName, texture);
        }

        public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
        {
            Command.SetGlobalBuffer(propertyName, GetBuffer(buffer));
        }

        public override void SetVector(string propertyName, Vector4 value)
        {
            Command.SetGlobalVector(propertyName, value);
        }
        public override void SetVectorArray(string propertyName, Vector4[] value)
        {
            Command.SetGlobalVectorArray(propertyName, value);
        }

        public override void SetFloat(string propertyName, float value)
        {
            Command.SetGlobalFloat(propertyName, value);
        }

        public override void SetFloatArray(string propertyName, float[] value)
        {
            Command.SetGlobalFloatArray(propertyName, value);
        }

        public override void SetInt(string propertyName, int value)
        {
            Command.SetGlobalInt(propertyName, value);
        }

        protected override void Execute()
        {
            // Does nothing (Eventually could do a command.setglobalbuffer or something?)
        }

        public override void SetMatrix(string propertyName, Matrix4x4 value)
        {
            Command.SetGlobalMatrix(propertyName, value);
        }

        public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value)
        {
            var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
            var size = descriptor.Count * descriptor.Stride;
            Command.SetGlobalConstantBuffer(GetBuffer(value), propertyName, 0, size);
        }

        public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
        {
            Command.SetGlobalMatrixArray(propertyName, value);
        }

        protected override void SetupTargets()
        {
        }
    }
}