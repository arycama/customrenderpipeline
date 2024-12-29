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

        var descriptor = new RtHandleDescriptor(resource.width, resource.height, resource.graphicsFormat, resource.volumeDepth, resource.dimension, false, resource.useMipMap, resource.autoGenerateMips);
        return new RTHandle(index, true, descriptor);
    }

    protected override bool DoesResourceMatchDescriptor(RenderTexture resource, RtHandleDescriptor descriptor)
    {
        var isDepth = GraphicsFormatUtility.IsDepthFormat(descriptor.Format);
        if ((isDepth && descriptor.Format != resource.depthStencilFormat) || (!isDepth && descriptor.Format != resource.graphicsFormat))
            return false;

        if (descriptor.IsScreenTexture)
        {
            // For screen textures, ensure we get a rendertexture that is the actual screen width/height
            if (resource.width != screenWidth || resource.height != screenHeight)
                return false;
        }
        else if (resource.width < descriptor.Width || resource.height < descriptor.Height)
            return false;

        if (resource.enableRandomWrite == descriptor.EnableRandomWrite && resource.dimension == descriptor.Dimension && resource.useMipMap == descriptor.HasMips)
        {
            if (descriptor.Dimension != TextureDimension.Tex2D && resource.volumeDepth < descriptor.VolumeDepth)
                return false;

            return true;
        }

        return false;
    }

    protected override RenderTexture CreateResource(RTHandle handle)
    {
        var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Descriptor.Format);
        var isStencil = handle.Descriptor.Format == GraphicsFormat.D32_SFloat_S8_UInt || handle.Descriptor.Format == GraphicsFormat.D24_UNorm_S8_UInt;

        var width = handle.Descriptor.IsScreenTexture ? screenWidth : handle.Descriptor.Width;
        var height = handle.Descriptor.IsScreenTexture ? screenHeight : handle.Descriptor.Height;

        var graphicsFormat = isDepth ? GraphicsFormat.None : handle.Descriptor.Format;
        var depthFormat = isDepth ? handle.Descriptor.Format : GraphicsFormat.None;
        var stencilFormat = isStencil ? GraphicsFormat.R8_UInt : GraphicsFormat.None;

        var result = new RenderTexture(width, height, graphicsFormat, depthFormat)
        {
            autoGenerateMips = false, // Always false, we manually handle mip generation if needed
            dimension = handle.Descriptor.Dimension,
            enableRandomWrite = handle.Descriptor.EnableRandomWrite,
            hideFlags = HideFlags.HideAndDontSave,
            stencilFormat = stencilFormat,
            useMipMap = handle.Descriptor.HasMips,
            volumeDepth = handle.Descriptor.VolumeDepth,
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
        return new RTHandle(handleIndex, isPersistent, descriptor);
    }
}