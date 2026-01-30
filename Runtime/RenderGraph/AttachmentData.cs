using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public readonly struct AttachmentData
{
    public readonly ResourceHandle<RenderTexture> handle;
    public readonly RenderTargetIdentifier target;
    public readonly GraphicsFormat frameBufferFormat;
    public readonly bool isFrameBufferOutput;

    public AttachmentData(ResourceHandle<RenderTexture> handle, RenderTargetIdentifier target, GraphicsFormat frameBufferFormat = default, bool isFrameBufferOutput = false)
    {
        this.handle = handle;
        this.target = target;
        this.frameBufferFormat = frameBufferFormat;
        this.isFrameBufferOutput = isFrameBufferOutput;
    }
}