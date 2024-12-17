using Arycama.CustomRenderPipeline;
using System;
using UnityEngine.Rendering;

public class RenderGraphBuilder<T> : RenderGraphBuilder
{
    public T Data { get; set; }
    private Action<CommandBuffer, RenderPass, T> pass;

    public void SetRenderFunction(Action<CommandBuffer, RenderPass, T> pass)
    {
        this.pass = pass;
    }

    public override void ClearRenderFunction()
    {
        pass = null;
    }

    public override void Execute(CommandBuffer command, RenderPass pass)
    {
        this.pass?.Invoke(command, pass, Data);
    }
}
