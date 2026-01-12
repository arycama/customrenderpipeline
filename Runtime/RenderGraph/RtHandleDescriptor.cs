using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public struct RtHandleDescriptor : IResourceDescriptor<RenderTexture>
{
	// TODO: Should we just use the Builtin renderTextureDescriptor struct?
	public int width;
	public int height;
	public GraphicsFormat format;
	public int volumeDepth;
	public TextureDimension dimension;
	public bool isScreenTexture;
	public bool hasMips;
	public bool autoGenerateMips;
	public bool enableRandomWrite;
	public bool isExactSize;
	public RTClearFlags clearFlags;
	public Color clearColor;
	public float clearDepth;
	public uint clearStencil;
	public VRTextureUsage vrTextureUsage;

	public RtHandleDescriptor(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool enableRandomWrite = false, bool isExactSize = false, RTClearFlags clearFlags = RTClearFlags.None, Color clearColor = default, float clearDepth = 1f, uint clearStencil = 0u, VRTextureUsage vrTextureUsage = VRTextureUsage.None)
	{
		this.width = width;
		this.height = height;
		this.format = format;
		this.volumeDepth = volumeDepth;
		this.dimension = dimension;
		this.isScreenTexture = isScreenTexture;
		this.hasMips = hasMips;
		this.autoGenerateMips = autoGenerateMips;
		this.enableRandomWrite = enableRandomWrite;
		this.isExactSize = isExactSize;
		this.clearFlags= clearFlags;
		this.clearColor = clearColor;
		this.clearDepth = clearDepth;
		this.clearStencil = clearStencil;
		this.vrTextureUsage = vrTextureUsage;
	}

	public readonly override string ToString() => $"{width}x{height}x{volumeDepth} {format} {dimension}";

	public readonly RenderTexture CreateResource(ResourceHandleSystemBase system)
	{
		var rtHandleSystem = system as RTHandleSystem;

		int width, height;
		if (isScreenTexture)
		{
			Assert.IsTrue(rtHandleSystem.ScreenWidth > 0);
			Assert.IsTrue(rtHandleSystem.ScreenHeight > 0);
			width = rtHandleSystem.ScreenWidth;
			height = rtHandleSystem.ScreenHeight;
		}
		else
		{
			width = this.width;
			height = this.height;
		}

		var isDepth = GraphicsFormatUtility.IsDepthFormat(format);
		var isStencil = GraphicsFormatUtility.IsStencilFormat(format);
		var graphicsFormat = isDepth ? GraphicsFormat.None : format;
		var depthFormat = isDepth ? format : GraphicsFormat.None;
		var stencilFormat = isStencil ? GraphicsFormat.R8_UInt : GraphicsFormat.None;

		var result = new RenderTexture(width, height, graphicsFormat, depthFormat)
		{
			autoGenerateMips = false, // Always false, we manually handle mip generation if needed
			dimension = dimension,
			enableRandomWrite = enableRandomWrite,
			hideFlags = HideFlags.HideAndDontSave,
			stencilFormat = stencilFormat,
			useMipMap = hasMips,
			volumeDepth = volumeDepth,
			vrUsage = vrTextureUsage
		};

		Assert.IsTrue(result.Create());

		return result;
	}
}
