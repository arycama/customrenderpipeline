using UnityEngine;

public class CameraTargetData : RTHandleData
{
	public CameraTargetData(ResourceHandle<RenderTexture> handle) : base(handle, "Input")
	{
	}
}
