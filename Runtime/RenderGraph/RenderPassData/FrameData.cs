using UnityEngine;

public class FrameData : ConstantBufferData
{
	public FrameData(ResourceHandle<GraphicsBuffer> buffer) : base(buffer, "FrameData")
	{
	}
}