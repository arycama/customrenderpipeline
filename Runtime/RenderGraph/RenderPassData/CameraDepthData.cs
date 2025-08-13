using UnityEngine;

public class CameraDepthData : RTHandleData
{
	public CameraDepthData(ResourceHandle<RenderTexture> handle) : base(handle, "Depth")
	{
	}
}
