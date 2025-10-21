using UnityEngine.Rendering;

public readonly struct TimeData : IRenderPassData
{
	public readonly double time;
	public readonly double previousTime;

	public TimeData(double time, double previousTime)
	{
		this.time = time;
		this.previousTime = previousTime;
	}

	public void SetInputs(RenderPassBase pass)
	{
	}

	public void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}