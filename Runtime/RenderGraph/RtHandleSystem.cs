using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
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

	protected override RtHandleDescriptor CreateDescriptorFromResource(RenderTexture resource) => new(resource.width, resource.height, resource.graphicsFormat, resource.volumeDepth, resource.dimension, false, resource.useMipMap, resource.autoGenerateMips);

	protected override bool DoesResourceMatchDescriptor(RenderTexture resource, RtHandleDescriptor descriptor)
	{
		var isDepth = GraphicsFormatUtility.IsDepthFormat(descriptor.format);
		if (isDepth && descriptor.format != resource.depthStencilFormat || !isDepth && descriptor.format != resource.graphicsFormat)
			return false;

		if (resource.dimension != descriptor.dimension)
			return false;

		if (resource.enableRandomWrite != descriptor.enableRandomWrite)
			return false;

		if (resource.useMipMap != descriptor.hasMips)
			return false;

		if (resource.volumeDepth < descriptor.volumeDepth)
			return false;

		if (descriptor.isScreenTexture)
		{
			// For screen textures, ensure we get a rendertexture that is the actual screen width/height
			if (resource.width != ScreenWidth || resource.height != ScreenHeight)
				return false;
		}
		else if (descriptor.isExactSize || descriptor.dimension == TextureDimension.Cube || descriptor.dimension == TextureDimension.CubeArray)
		{
			// Some textures need exact size. (Eg writing to multiple targets at non-screen resolution
			if (resource.width != descriptor.width || resource.height != descriptor.height || resource.volumeDepth != descriptor.volumeDepth)
				return false;
		}
		else if (resource.width < descriptor.width || resource.height < descriptor.height)
			return false;

		return true;
	}
}