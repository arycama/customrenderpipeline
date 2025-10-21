using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class IndirectComputeRenderPass : BaseComputeRenderPass<IndirectComputeRenderPass>
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
		foreach (var keyword in keywords)
			Command.EnableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

		Command.DispatchCompute(computeShader, kernelIndex, GetBuffer(indirectBuffer), argsOffset * 4);

		foreach (var keyword in keywords)
			Command.DisableKeyword(computeShader, new LocalKeyword(computeShader, keyword));

		keywords.Clear();
	}

	protected override void ExecuteRenderPassBuilder()
	{
		Assert.IsFalse(hasDefault && hasData);

		if (hasDefault)
			renderGraphBuilderDefault.Execute(Command, this);

		if (hasData)
			renderGraphBuilder.Execute(Command, this);
	}
}