using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    /// <summary>
    /// Utility class for an IRenderPassData that contains a single RTHandle
    /// </summary>
    public class RTHandleData : IRenderPassData
    {
        public RTHandle Handle { get; }
        private readonly string propertyName, scaleLimitPropertyName;

        public RTHandleData(RTHandle handle, string propertyName)
        {
            Handle = handle;
            this.propertyName = propertyName;
            scaleLimitPropertyName = propertyName + "ScaleLimit";
        }

        public static implicit operator RTHandle(RTHandleData data) => data.Handle;

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture(propertyName, Handle);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(scaleLimitPropertyName, Handle.ScaleLimit2D);
        }
    }
}