using System;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public abstract class RenderPass<T> : RenderPassBase where T : RenderPass<T>
{
	protected readonly RenderGraphBuilderBase<T> renderGraphBuilderDefault = new RenderGraphBuilderBase<T>();
	protected RenderGraphBuilderBase<T> renderGraphBuilder;
	protected bool hasDefault, hasData;

	public override void Reset()
	{
		base.Reset();

		if (hasDefault)
		{
			renderGraphBuilderDefault.ClearRenderFunction();
			hasDefault = false;
		}

		if (hasData)
		{
			renderGraphBuilder.ClearRenderFunction();
			hasData = false;
		}
	}

	public void SetRenderFunction(Action<CommandBuffer, T> pass)
	{
		Assert.IsFalse(hasData);
		renderGraphBuilderDefault.SetRenderFunction(pass);
		hasDefault = true;
	}

	public void SetRenderFunction<K>(K data, Action<CommandBuffer, T, K> pass)
	{
		Assert.IsFalse(hasDefault);
		var result = new RenderGraphBuilder<T, K>();
		result.Data = data;
		result.SetRenderFunction(pass);
		renderGraphBuilder = result;
		hasData = true;
	}
}
