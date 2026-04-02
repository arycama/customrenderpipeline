using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public readonly struct AttachmentData
{
    public readonly ResourceHandle<RenderTexture> handle;
    public readonly RenderTargetIdentifier target;
    public readonly GraphicsFormat frameBufferFormat;
    public readonly bool isFrameBufferOutput;
    public readonly int mipLevel;
    public readonly CubemapFace cubemapFace;
    public readonly int depthSlice;

    public AttachmentData(ResourceHandle<RenderTexture> handle, RenderTargetIdentifier target, GraphicsFormat frameBufferFormat = default, bool isFrameBufferOutput = false, int mipLevel = 0, CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1)
    {
        this.handle = handle;
        this.target = target;
        this.frameBufferFormat = frameBufferFormat;
        this.isFrameBufferOutput = isFrameBufferOutput;
        this.mipLevel = mipLevel;
        this.cubemapFace = cubemapFace;
        this.depthSlice = depthSlice;
    }
}