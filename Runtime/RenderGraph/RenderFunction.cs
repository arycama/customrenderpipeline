using System;
using UnityEngine.Rendering;

public readonly struct RenderFunction<K> : IRenderFunction
{
	private readonly Action<CommandBuffer, RenderPass, K> renderFunction;

	public RenderFunction(Action<CommandBuffer, RenderPass, K> renderFunction)
	{
		this.renderFunction = renderFunction;
	}

	void IRenderFunction.Execute(CommandBuffer command, RenderPass pass, object data)
	{
		renderFunction(command, pass, (K)data);
	}
}
