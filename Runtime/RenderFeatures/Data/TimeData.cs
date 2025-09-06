using UnityEngine.Rendering;

public class TimeData : IRenderPassData
{
	public double Time { get; }
	public double PreviousTime { get; }

	public TimeData(double time, double previousTime)
	{
		Time = time;
		PreviousTime = previousTime;
	}

	public void SetInputs(RenderPassBase pass)
	{
	}

	public void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}