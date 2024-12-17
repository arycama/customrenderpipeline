using Arycama.CustomRenderPipeline;
using System;
using UnityEngine.Rendering;

public class RenderGraphBuilder
{
    private Action<CommandBuffer, RenderPass> pass;

    public void SetRenderFunction(Action<CommandBuffer, RenderPass> pass)
    {
        this.pass = pass;
    }

    public virtual void ClearRenderFunction()
    {
        pass = null;
    }

    public virtual void Execute(CommandBuffer command, RenderPass pass)
    {
        this.pass?.Invoke(command, pass);
    }
}
