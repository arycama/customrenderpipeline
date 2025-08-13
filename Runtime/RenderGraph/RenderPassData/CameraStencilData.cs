using UnityEngine;
using UnityEngine.Rendering;

public class CameraStencilData : RTHandleData
{
	public CameraStencilData(ResourceHandle<RenderTexture> handle) : base(handle, "Stencil", subElement: RenderTextureSubElement.Stencil)
	{
	}
}
