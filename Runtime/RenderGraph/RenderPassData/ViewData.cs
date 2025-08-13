using UnityEngine;

public class ViewData : ConstantBufferData
{
	public ViewData(ResourceHandle<GraphicsBuffer> buffer) : base(buffer, "ViewData")
	{
	}
}
