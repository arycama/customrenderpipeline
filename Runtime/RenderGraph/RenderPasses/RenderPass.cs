using System;
using UnityEngine.Rendering;

public abstract class RenderPass<T> : RenderPassBase where T : RenderPass<T>
{
	protected RenderGraphBuilderBase<T> renderGraphBuilder;

	public override void Reset()
	{
		base.Reset();
		renderGraphBuilder?.ClearRenderFunction();
	}

	public void SetRenderFunction(Action<CommandBuffer, T> pass)
	{
		var result = new RenderGraphBuilderBase<T>();
		result.SetRenderFunction(pass);
		renderGraphBuilder = result;
	}

	public void SetRenderFunction<K>(K data, Action<CommandBuffer, T, K> pass)
	{
		var result = new RenderGraphBuilder<T, K>();
		result.Data = data;
		result.SetRenderFunction(pass);
		renderGraphBuilder = result;
	}
}
