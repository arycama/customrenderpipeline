using UnityEngine.Rendering;

public class TimeData : IRenderPassData
{
	public double Time { get; }
	public double PreviousTime { get; }
	public double DeltaTime { get; }

	public TimeData(double time, double previousTime, double deltaTime)
	{
		Time = time;
		PreviousTime = previousTime;
		DeltaTime = deltaTime;
	}

	public void SetInputs(RenderPassBase pass)
	{
	}

	public void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}