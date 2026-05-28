using System;
using UnityEngine.Rendering;

public class GenericFrameRenderFeature : FrameRenderFeature
{
    public delegate void RenderFunction(ScriptableRenderContext context);

    private readonly RenderFunction render;

	public GenericFrameRenderFeature(RenderGraph renderGraph, RenderFunction render) : base(renderGraph)
	{
		this.render = render;
	}

	public override void Render(ScriptableRenderContext context)
	{
		render(context);
	}
}
