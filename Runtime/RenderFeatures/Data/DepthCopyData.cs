using UnityEngine;

public class DepthCopyData : RTHandleData
{
	public DepthCopyData(ResourceHandle<RenderTexture> handle) : base(handle, "_DepthCopy")
	{
	}
}