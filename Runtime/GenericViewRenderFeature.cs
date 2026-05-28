using System;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class GenericViewRenderFeature : ViewRenderFeature
{
    public delegate void RenderFunction(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context);

    private readonly RenderFunction render;

	public GenericViewRenderFeature(RenderGraph renderGraph, RenderFunction render) : base(renderGraph)
	{
		Assert.IsNotNull(render);
		this.render = render;
	}

	public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
		render(viewParameters, viewPassData, displayOutputData, context);
	}
}