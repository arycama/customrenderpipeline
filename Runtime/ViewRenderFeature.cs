/// <summary> Render feature that executes once per camera </summary>
public abstract class ViewRenderFeature : RenderFeatureBase
{
	public ViewRenderFeature(RenderGraph renderGraph) : base(renderGraph) { }

	public abstract void Render(ViewRenderData renderLoopData);
}
