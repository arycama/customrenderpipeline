using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RTHandleSystem : ResourceHandleSystem<RenderTexture, RtHandleDescriptor>
{
    private int screenWidth, screenHeight;

    public void SetScreenSize(int width, int height)
    {
        screenWidth = Mathf.Max(width, screenWidth);
        screenHeight = Mathf.Max(height, screenHeight);
    }

    protected override void DestroyResource(RenderTexture resource) => Object.DestroyImmediate(resource);

    protected override ResourceHandle<RenderTexture> CreateHandle(int handleIndex, bool isPersistent) => new(handleIndex, isPersistent);

    protected override RtHandleDescriptor CreateDescriptorFromResource(RenderTexture resource) => new(resource.width, resource.height, resource.graphicsFormat, resource.volumeDepth, resource.dimension, false, resource.useMipMap, resource.autoGenerateMips);

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

    protected override RenderTexture CreateResource(RtHandleDescriptor descriptor)
    {
        var isDepth = GraphicsFormatUtility.IsDepthFormat(descriptor.Format);
        var isStencil = descriptor.Format == GraphicsFormat.D32_SFloat_S8_UInt || descriptor.Format == GraphicsFormat.D24_UNorm_S8_UInt;

        var width = descriptor.IsScreenTexture ? screenWidth : descriptor.Width;
        var height = descriptor.IsScreenTexture ? screenHeight : descriptor.Height;

        var graphicsFormat = isDepth ? GraphicsFormat.None : descriptor.Format;
        var depthFormat = isDepth ? descriptor.Format : GraphicsFormat.None;
        var stencilFormat = isStencil ? GraphicsFormat.R8_UInt : GraphicsFormat.None;

        var result = new RenderTexture(width, height, graphicsFormat, depthFormat)
        {
            autoGenerateMips = false, // Always false, we manually handle mip generation if needed
            dimension = descriptor.Dimension,
            enableRandomWrite = descriptor.EnableRandomWrite,
            hideFlags = HideFlags.HideAndDontSave,
            stencilFormat = stencilFormat,
            useMipMap = descriptor.HasMips,
            volumeDepth = descriptor.VolumeDepth,
        };

        _ = result.Create();

        return result;
    }
}