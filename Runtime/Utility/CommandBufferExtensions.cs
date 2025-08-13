using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public static class CommandBufferExtensions
{
	public static void DispatchNormalized(this CommandBuffer commandBuffer, ComputeShader computeShader, int kernelIndex, int threadsX, int threadsY, int threadsZ)
	{
		computeShader.GetKernelThreadGroupSizes(kernelIndex, out var x, out var y, out var z);

		var threadGroupsX = Math.DivRoundUp(threadsX, (int)x);
		var threadGroupsY = Math.DivRoundUp(threadsY, (int)y);
		var threadGroupsZ = Math.DivRoundUp(threadsZ, (int)z);

		Assert.IsTrue(threadGroupsX > 0);
		Assert.IsTrue(threadGroupsY > 0);
		Assert.IsTrue(threadGroupsZ > 0);

		commandBuffer.DispatchCompute(computeShader, kernelIndex, threadGroupsX, threadGroupsY, threadGroupsZ);
	}

	public static CommandBufferProfilerScope ProfilerScope(this CommandBuffer command, string name)
	{
		return new CommandBufferProfilerScope(command, name);
	}
}
