using System;
using UnityEngine;
using UnityEngine.Rendering;

public class IndirectComputeRenderPass<T> : BaseComputeRenderPass<T>
{
	private uint argsOffset;
	private ResourceHandle<GraphicsBuffer> indirectBuffer;

	public void Initialize(ComputeShader computeShader, ResourceHandle<GraphicsBuffer> indirectBuffer, int kernelIndex = 0, uint argsOffset = 0)
	{
		this.computeShader = computeShader ?? throw new ArgumentNullException(nameof(computeShader));
		this.kernelIndex = kernelIndex;
		this.indirectBuffer = indirectBuffer;
		this.argsOffset = argsOffset;

		ReadBuffer("_IndirectArgsInput", indirectBuffer);
	}

	protected override void Execute()
	{
        for (var i = 0; i < colorBindings.Count; i++)
            Command.SetComputeTextureParam(computeShader, kernelIndex, colorBindings[i].Item2, GetRenderTexture(colorBindings[i].Item1), colorBindings[i].Item3);

        foreach (var keyword in keywords)
			Command.EnableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

		Command.DispatchCompute(computeShader, kernelIndex, GetBuffer(indirectBuffer), argsOffset * 4);

		foreach (var keyword in keywords)
			Command.DisableKeyword(computeShader, new LocalKeyword(computeShader, keyword));
	}
}