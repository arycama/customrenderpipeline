using System;
using UnityEngine.Rendering;

public class RenderGraphBuilder<T, K> : RenderGraphBuilderBase<T> where T : RenderPassBase
{
	public K Data { get; set; }
	private Action<CommandBuffer, T, K> pass;

	public void SetRenderFunction(Action<CommandBuffer, T, K> pass)
	{
		this.pass = pass;
	}

	public override void ClearRenderFunction()
	{
		pass = null;
	}

	public override void Execute(CommandBuffer command, T pass)
	{
		this.pass?.Invoke(command, pass, Data);
	}
}
