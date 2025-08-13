using System;
using UnityEngine.Rendering;

public class GenericFrameRenderFeature : FrameRenderFeature
{
	private Action<ScriptableRenderContext> render;
	private string profilerName;

	public GenericFrameRenderFeature(RenderGraph renderGraph, string profilerName, Action<ScriptableRenderContext> render) : base(renderGraph)
	{
		this.profilerName = profilerName;
		this.render = render;
	}

	public override string ProfilerNameOverride => profilerName;

	public override void Render(ScriptableRenderContext context)
	{
		render(context);
	}
}
