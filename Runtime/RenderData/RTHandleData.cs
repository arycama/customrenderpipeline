﻿using UnityEngine.Rendering;

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
            this.Handle = handle;
            this.propertyName = propertyName;
            this.scaleLimitPropertyName = propertyName + "ScaleLimit";
        }

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