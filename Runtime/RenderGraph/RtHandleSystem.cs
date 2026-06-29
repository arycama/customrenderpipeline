using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Unmath;
using Object = UnityEngine.Object;

public class RTHandleSystem : ResourceHandleSystem<RenderTexture, RtHandleDescriptor>
{
	public int ScreenWidth { get; private set; }
	public int ScreenHeight { get; private set; }
	public Int2 ScreenSize => new(ScreenWidth, ScreenHeight);

    public void SetScreenSize(int width, int height)
	{
		Assert.IsTrue(width > 0);
		Assert.IsTrue(height > 0);

		ScreenWidth = Math.Max(width, ScreenWidth);
		ScreenHeight = Math.Max(height, ScreenHeight);
	}

	public void ResetScreenSize()
	{
		ScreenWidth = 0;
		ScreenHeight = 0;
	}

	protected override void DestroyResource(RenderTexture resource) => Object.DestroyImmediate(resource);

	protected override RtHandleDescriptor CreateDescriptorFromResource(RenderTexture resource) => new(resource.width, resource.height, resource.graphicsFormat, resource.volumeDepth, resource.dimension, false, resource.useMipMap, resource.autoGenerateMips, resource.enableRandomWrite, true, vrUsage: resource.vrUsage, antiAliasing: resource.antiAliasing, isCcw: resource.descriptor.flags.HasFlag(RenderTextureCreationFlags.AllowVerticalFlip));

	protected override bool DoesResourceMatchDescriptor(RenderTexture resource, RtHandleDescriptor descriptor)
	{
        var resourceDescriptor = resource.descriptor;
		var isDepth = GraphicsFormatUtility.IsDepthFormat(descriptor.format);
		if (isDepth && descriptor.format != resourceDescriptor.depthStencilFormat || !isDepth && descriptor.format != resourceDescriptor.graphicsFormat)
			return false;

		if (resourceDescriptor.dimension != descriptor.dimension)
			return false;

		if (resourceDescriptor.enableRandomWrite != descriptor.enableRandomWrite)
			return false;

		if (resourceDescriptor.useMipMap != descriptor.hasMips)
			return false;

		if (resourceDescriptor.volumeDepth < descriptor.volumeDepth)
			return false;

        if (resourceDescriptor.msaaSamples != descriptor.antiAliasing)
            return false;

		if (descriptor.isScreenTexture)
		{
			// For screen textures, ensure we get a rendertexture that is the actual screen width/height
			if (resourceDescriptor.width != ScreenWidth || resourceDescriptor.height != ScreenHeight)
				return false;
		}
		else if (descriptor.isExactSize || descriptor.dimension == TextureDimension.Cube || descriptor.dimension == TextureDimension.CubeArray)
		{
			// Some textures need exact size. (Eg writing to multiple targets at non-screen resolution
			if (resourceDescriptor.width != descriptor.width || resourceDescriptor.height != descriptor.height || resourceDescriptor.volumeDepth != descriptor.volumeDepth)
				return false;
		}
		else if (resourceDescriptor.width < descriptor.width || resourceDescriptor.height < descriptor.height)
			return false;

        if (descriptor.isCcw && !resourceDescriptor.flags.HasFlag(RenderTextureCreationFlags.AllowVerticalFlip))
            return false;

		return true;
	}
}