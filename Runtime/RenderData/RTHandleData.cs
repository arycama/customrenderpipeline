﻿using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    /// <summary>
    /// Utility class for an IRenderPassData that contains a single ResourceHandle<RenderTexture>
    /// </summary>
    public class RTHandleData : IRenderPassData
    {
        public ResourceHandle<RenderTexture> Handle { get; }
        private int propertyNameId, scaleLimitPropertyId;
        private readonly int mip;
        private readonly RenderTextureSubElement subElement;

        public RTHandleData(ResourceHandle<RenderTexture> handle, string propertyName, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
        {
            Handle = handle;
            this.mip = mip;
            this.subElement = subElement;
            propertyNameId = Shader.PropertyToID(propertyName);
            scaleLimitPropertyId = Shader.PropertyToID($"{propertyName}ScaleLimit");
        }

        public static implicit operator ResourceHandle<RenderTexture>(RTHandleData data) => data.Handle;

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture(propertyNameId, Handle, mip, subElement);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(scaleLimitPropertyId, pass.GetScaleLimit2D(Handle));
        }
    }
}