using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class GenericRenderPass : RenderPass
    {
        public override void SetTexture(CommandBuffer command, string propertyName, Texture texture)
        {
            // Should also clean up on post render.. but 
            command.SetGlobalTexture(propertyName, texture);
        }

        public override void SetTexture(CommandBuffer command, string propertyName, RTHandle texture)
        {
            // Should also clean up on post render.. but 
            command.SetGlobalTexture(propertyName, texture);
        }

        public override void SetBuffer(CommandBuffer command, string propertyName, GraphicsBuffer buffer)
        {
            command.SetGlobalBuffer(propertyName, buffer);
        }

        public override void SetVector(CommandBuffer command, string propertyName, Vector4 value)
        {
            command.SetGlobalVector(propertyName, value);
        }

        public override void SetFloat(CommandBuffer command, string propertyName, float value)
        {
            command.SetGlobalFloat(propertyName, value);
        }

        public override void SetInt(CommandBuffer command, string propertyName, int value)
        {
            command.SetGlobalInt(propertyName, value);
        }
    }
}