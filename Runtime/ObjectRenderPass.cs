using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class ObjectRenderPass : RenderPass
    {
        public override void SetTexture(CommandBuffer command, string propertyName, Texture texture)
        {
            command.SetGlobalTexture(propertyName, texture);
            //postRender.Add(cmd => cmd.SetGlobalTexture(propertyName, BuiltinRenderTextureType.None));
        }

        public override void SetTexture(CommandBuffer command, string propertyName, RTHandle texture)
        {
            command.SetGlobalTexture(propertyName, texture);
            //postRender.Add(cmd => cmd.SetGlobalTexture(propertyName, BuiltinRenderTextureType.None));
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer)
        {
            command.SetGlobalBuffer(propertyName, buffer);
            //postRender.Add(cmd => cmd.SetGlobalBuffer(propertyName, (GraphicsBuffer)null));
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            command.SetGlobalVector(propertyName, value);
            //postRender.Add(cmd => cmd.SetGlobalVector(propertyName, Vector4.zero));
        }

        public override void SetFloat(CommandBuffer command, string propertyName, float value)
        {
            command.SetGlobalFloat(propertyName, value);
            //postRender.Add(cmd => cmd.SetGlobalFloat(propertyName, 0.0f));
        }

        public override void SetInt(CommandBuffer command, string propertyName, int value)
        {
            command.SetGlobalInt(propertyName, value);
            //postRender.Add(cmd => cmd.SetGlobalInt(propertyName, 0));
        }
    }
}