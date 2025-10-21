using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Utility class for an IRenderPassData that contains a single ResourceHandle<RenderTexture>
/// </summary>
public readonly struct RTHandleData : IRenderPassData
{
	public readonly ResourceHandle<RenderTexture> handle;
	private readonly int propertyNameId, scaleLimitPropertyId;
	private readonly int mip;
	private readonly RenderTextureSubElement subElement;

	public RTHandleData(ResourceHandle<RenderTexture> handle, string propertyName, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		this.handle = handle;
		this.mip = mip;
		this.subElement = subElement;
		propertyNameId = Shader.PropertyToID(propertyName);
		scaleLimitPropertyId = Shader.PropertyToID($"{propertyName}ScaleLimit");
	}

	public void SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture(propertyNameId, handle, mip, subElement);
	}

	public void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
		pass.SetVector(scaleLimitPropertyId, pass.GetScaleLimit2D(handle));
	}
}
