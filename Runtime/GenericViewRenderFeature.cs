using System;
using UnityEngine.Assertions;

public class GenericViewRenderFeature : ViewRenderFeature
{
	private readonly Action<ViewRenderData> render;

	public GenericViewRenderFeature(RenderGraph renderGraph, Action<ViewRenderData> render) : base(renderGraph)
	{
		Assert.IsNotNull(render);
		this.render = render;
	}

	public override void Render(ViewRenderData camera)
    {
		render(camera);
	}
}