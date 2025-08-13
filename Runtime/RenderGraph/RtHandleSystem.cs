using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RTHandleSystem : ResourceHandleSystem<RenderTexture, RtHandleDescriptor>
{
	public int ScreenWidth { get; private set; }
	public int ScreenHeight { get; private set; }

	public void SetScreenSize(int width, int height)
	{
		ScreenWidth = Mathf.Max(width, ScreenWidth);
		ScreenHeight = Mathf.Max(height, ScreenHeight);
	}

	protected override void DestroyResource(RenderTexture resource) => Object.DestroyImmediate(resource);

	protected override RtHandleDescriptor CreateDescriptorFromResource(RenderTexture resource) => new(resource.width, resource.height, resource.graphicsFormat, resource.volumeDepth, resource.dimension, false, resource.useMipMap, resource.autoGenerateMips);

	protected override bool DoesResourceMatchDescriptor(RenderTexture resource, RtHandleDescriptor descriptor)
	{
		var isDepth = GraphicsFormatUtility.IsDepthFormat(descriptor.Format);
		if (isDepth && descriptor.Format != resource.depthStencilFormat || !isDepth && descriptor.Format != resource.graphicsFormat)
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
			if (resource.width != ScreenWidth || resource.height != ScreenHeight)
				return false;
		}
		else if (descriptor.IsExactSize || descriptor.Dimension == TextureDimension.Cube || descriptor.Dimension == TextureDimension.CubeArray)
		{
			// Some textures need exact size. (Eg writing to multiple targets at non-screen resolution
			if (resource.width != descriptor.Width || resource.height != descriptor.Height)
				return false;
		}
		else if (resource.width < descriptor.Width || resource.height < descriptor.Height)
			return false;

		return true;
	}
}