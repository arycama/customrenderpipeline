using System.Collections.Generic;
using UnityEngine.Rendering;

public delegate void RenderGraphPass(CommandBuffer command, ScriptableRenderContext context);

public class RenderGraph
{
    private List<RenderGraphPass> actions = new();

    public void AddRenderPass(RenderGraphPass pass)
    {
        actions.Add(pass);
    }

    public void Execute(CommandBuffer command, ScriptableRenderContext context)
    {
        foreach(var action in actions)
        {
            action(command, context);
        }

        actions.Clear();
    }
}

public class CullingResultsHandle
{
    public CullingResults CullingResults { get; set; }

    public static implicit operator CullingResults(CullingResultsHandle cullingResultsHandle) => cullingResultsHandle.CullingResults;
}

public struct RenderGraphBuilder
{

}