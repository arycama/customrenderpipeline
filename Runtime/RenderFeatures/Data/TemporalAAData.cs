using UnityEngine;

public class TemporalAAData : ConstantBufferData
{
	public TemporalAAData(ResourceHandle<GraphicsBuffer> buffer) : base(buffer, "TemporalProperties")
	{
	}
}