using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public readonly struct AttachmentData
{
    public readonly ResourceHandle<RenderTexture> handle;
    public readonly RenderTargetIdentifier? frameBufferTarget;
    public readonly GraphicsFormat frameBufferFormat;
    public readonly int mipLevel;
    public readonly CubemapFace cubemapFace;
    public readonly int depthSlice;

    public AttachmentData(ResourceHandle<RenderTexture> handle, RenderTargetIdentifier? frameBufferTarget = default, GraphicsFormat frameBufferFormat = default, bool isFrameBufferOutput = false, int mipLevel = 0, CubemapFace cubemapFace = CubemapFace.Unknown, int depthSlice = -1)
    {
        this.handle = handle;
        this.frameBufferTarget = frameBufferTarget;
        this.frameBufferFormat = frameBufferFormat;
        this.mipLevel = mipLevel;
        this.cubemapFace = cubemapFace;
        this.depthSlice = depthSlice;
    }
}