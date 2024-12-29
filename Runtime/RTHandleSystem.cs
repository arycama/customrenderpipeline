using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RTHandleSystem : ResourceHandleSystem<RenderTexture, RTHandle, RtHandleDescriptor>
{
    private int screenWidth, screenHeight;

    public void SetScreenSize(int width, int height)
    {
        screenWidth = Mathf.Max(width, screenWidth);
        screenHeight = Mathf.Max(height, screenHeight);
    }

    protected override RTHandle CreateHandleFromResource(RenderTexture resource, int index)
    {
        // Ensure its created (Can happen with some RenderTextures that are imported as soon as created
        if (!resource.IsCreated())
            _ = resource.Create();

        return new RTHandle(index, true)
        {
            Width = resource.width,
            Height = resource.height,
            Format = resource.graphicsFormat,
            EnableRandomWrite = resource.enableRandomWrite,
            VolumeDepth = resource.volumeDepth,
            Dimension = resource.dimension,
            HasMips = resource.useMipMap,
            AutoGenerateMips = resource.autoGenerateMips,
            IsScreenTexture = false,
        };
    }

    protected override bool DoesResourceMatchHandle(RenderTexture resource, RTHandle handle)
    {
        var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
        if ((isDepth && handle.Format != resource.depthStencilFormat) || (!isDepth && handle.Format != resource.graphicsFormat))
            return false;

        if (handle.IsScreenTexture)
        {
            // For screen textures, ensure we get a rendertexture that is the actual screen width/height
            if (resource.width != screenWidth || resource.height != screenHeight)
                return false;
        }
        else if (resource.width < handle.Width || resource.height < handle.Height)
            return false;

        if (resource.enableRandomWrite == handle.EnableRandomWrite && resource.dimension == handle.Dimension && resource.useMipMap == handle.HasMips)
        {
            if (handle.Dimension != TextureDimension.Tex2D && resource.volumeDepth < handle.VolumeDepth)
                return false;

            return true;
        }

        return false;
    }

    protected override RenderTexture CreateResource(RTHandle handle)
    {
        var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
        var isStencil = handle.Format == GraphicsFormat.D32_SFloat_S8_UInt || handle.Format == GraphicsFormat.D24_UNorm_S8_UInt;

        var width = handle.IsScreenTexture ? screenWidth : handle.Width;
        var height = handle.IsScreenTexture ? screenHeight : handle.Height;

        var graphicsFormat = isDepth ? GraphicsFormat.None : handle.Format;
        var depthFormat = isDepth ? handle.Format : GraphicsFormat.None;
        var stencilFormat = isStencil ? GraphicsFormat.R8_UInt : GraphicsFormat.None;

        var result = new RenderTexture(width, height, graphicsFormat, depthFormat) 
        { 
            autoGenerateMips = false, // Always false, we manually handle mip generation if needed
            dimension = handle.Dimension,
            enableRandomWrite = handle.EnableRandomWrite, 
            hideFlags = HideFlags.HideAndDontSave,
            name = $"{handle.Dimension} {handle.Format} {width}x{height} {resourceCount++}",
            stencilFormat = stencilFormat,
            useMipMap = handle.HasMips,
            volumeDepth = handle.VolumeDepth,
        };

        _ = result.Create();

        return result;
    }

    protected override void DestroyResource(RenderTexture resource)
    {
        Object.DestroyImmediate(resource);
    }

    protected override RTHandle CreateHandleFromDescriptor(RtHandleDescriptor descriptor, bool isPersistent, int handleIndex)
    {
        return new RTHandle(handleIndex, isPersistent)
        {
            Width = descriptor.Width,
            Height = descriptor.Height,
            Format = descriptor.Format,
            VolumeDepth = descriptor.VolumeDepth,
            Dimension = descriptor.Dimension,
            IsScreenTexture = descriptor.IsScreenTexture,
            HasMips = descriptor.HasMips,
            AutoGenerateMips = descriptor.AutoGenerateMips,
            // This gets set automatically if a texture is written to by a compute shader
            EnableRandomWrite = false
        };
    }
}