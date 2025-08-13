using UnityEngine;

public class PerCascadeData : ConstantBufferData
{
	public PerCascadeData(ResourceHandle<GraphicsBuffer> buffer) : base(buffer, "PerCascadeData")
	{
	}
}
