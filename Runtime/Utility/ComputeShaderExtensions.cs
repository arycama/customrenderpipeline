using UnityEngine;

public static class ComputeShaderExtensions
{
	public static void GetThreadGroupSizes(this ComputeShader computeShader, int kernelIndex, Vector3Int threads, out uint groupsX, out uint groupsY, out uint groupsZ)
	{
		computeShader.GetKernelThreadGroupSizes(kernelIndex, out var x, out var y, out var z);
		groupsX = Math.DivRoundUp((uint)threads.x, x);
		groupsY = Math.DivRoundUp((uint)threads.y, y);
		groupsZ = Math.DivRoundUp((uint)threads.z, z);
	}

	public static void GetThreadGroupSizes(this ComputeShader computeShader, int kernelIndex, Vector2Int threads, out uint groupsX, out uint groupsY)
	{
		computeShader.GetThreadGroupSizes(kernelIndex, new Vector3Int(threads.x, threads.y, 1), out groupsX, out groupsY, out _);
	}

	public static void GetThreadGroupSizes(this ComputeShader computeShader, int kernelIndex, int threadsX, out uint groupsX)
	{
		computeShader.GetThreadGroupSizes(kernelIndex, new Vector3Int(threadsX, 1, 1), out groupsX, out _, out _);
	}

	public static void DispatchNormalized(this ComputeShader computeShader, int kernelIndex, int threadsX, int threadsY, int threadsZ)
	{
		computeShader.GetThreadGroupSizes(kernelIndex, new(threadsX, threadsY, threadsZ), out var groupsX, out var groupsY, out var groupsZ);
		computeShader.Dispatch(kernelIndex, (int)groupsX, (int)groupsY, (int)groupsZ);
	}

	public static void ToggleKeyword(this ComputeShader computeShader, string keyword, bool isEnabled)
	{
		if (isEnabled) computeShader.EnableKeyword(keyword);
		else computeShader.DisableKeyword(keyword);
	}

	public static void DispatchNormalized(this ComputeShader computeShader, int kernelIndex, Vector3Int threadGroups)
	{
		computeShader.DispatchNormalized(kernelIndex, threadGroups.x, threadGroups.y, threadGroups.z);
	}
}