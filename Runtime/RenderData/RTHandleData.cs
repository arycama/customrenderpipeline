using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    /// <summary>
    /// Utility class for an IRenderPassData that contains a single ResourceHandle<RenderTexture>
    /// </summary>
    public class RTHandleData : IRenderPassData
    {
        public ResourceHandle<RenderTexture> Handle { get; }
        private readonly string propertyName, scaleLimitPropertyName;

        public RTHandleData(ResourceHandle<RenderTexture> handle, string propertyName)
        {
            Handle = handle;
            this.propertyName = propertyName;
            scaleLimitPropertyName = propertyName + "ScaleLimit";
        }

        public static implicit operator ResourceHandle<RenderTexture>(RTHandleData data) => data.Handle;

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture(propertyName, Handle);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(scaleLimitPropertyName, pass.GetScaleLimit2D(Handle));
        }
    }
}