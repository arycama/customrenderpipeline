using UnityEngine;

public class CameraTargetData : RTHandleData, IRtHandleId
{
	string IRtHandleId.Id => "Input";

	public CameraTargetData(ResourceHandle<RenderTexture> handle) : base(handle, "Input")
	{
	}
}
