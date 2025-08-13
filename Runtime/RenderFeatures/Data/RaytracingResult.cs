using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public struct RaytracingResult : IRenderPassData
{
	public RayTracingAccelerationStructure Rtas { get; private set; }
	public float Bias { get; private set; }
	public float DistantBias { get; private set; }

	public RaytracingResult(RayTracingAccelerationStructure rtas, float bias, float distantBias)
	{
		Rtas = rtas;
		Bias = bias;
		DistantBias = distantBias;
	}

	public readonly void SetInputs(RenderPassBase pass)
	{
		// TODO: RTAS input handling
	}

	public readonly void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
	}
}