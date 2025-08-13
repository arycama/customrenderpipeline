using System;
using UnityEngine.Rendering;

public class RenderGraphBuilderBase<T> where T : RenderPassBase
{
	private Action<CommandBuffer, T> pass;

	public void SetRenderFunction(Action<CommandBuffer, T> pass)
	{
		this.pass = pass;
	}

	public virtual void ClearRenderFunction()
	{
		pass = null;
	}

	public virtual void Execute(CommandBuffer command, T pass)
	{
		this.pass?.Invoke(command, pass);
	}
}
