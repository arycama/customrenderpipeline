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

    protected override RtHandleDescriptor CreateDescriptorFromResource(RenderTexture resource) => new(resource.width, resource.height, resource.graphicsFormat, resource.volumeDepth, resource.dimension, false, resource.useMipMap, resource.autoGenerateMips);

    protected override bool DoesResourceMatchDescriptor(RenderTexture resource, RtHandleDescriptor descriptor)
    {
        var isDepth = GraphicsFormatUtility.IsDepthFormat(descriptor.Format);
        if ((isDepth && descriptor.Format != resource.depthStencilFormat) || (!isDepth && descriptor.Format != resource.graphicsFormat))
            return false;

        if (resource.dimension != descriptor.Dimension)
            return false;

        if (resource.enableRandomWrite != descriptor.EnableRandomWrite)
            return false;

        if (resource.useMipMap != descriptor.HasMips)
            return false;

        if (resource.volumeDepth < descriptor.VolumeDepth)
            return false;

        if (descriptor.IsScreenTexture)
        {
            // For screen textures, ensure we get a rendertexture that is the actual screen width/height
            if (resource.width != screenWidth || resource.height != screenHeight)
                return false;
        }
        else if (resource.width < descriptor.Width || resource.height < descriptor.Height)
            return false;

        return true;
    }
}