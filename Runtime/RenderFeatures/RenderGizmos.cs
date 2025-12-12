using UnityEngine.Rendering;

public class RenderGizmos : ViewRenderFeature
{
	public RenderGizmos(RenderGraph renderGraph) : base(renderGraph)
	{
	}

	public override void Render(ViewRenderData viewRenderData)
    {
#if UNITY_EDITOR
		if (!UnityEditor.Handles.ShouldRenderGizmos())
			return;

		var preImageEffects = viewRenderData.context.CreateGizmoRendererList(viewRenderData.camera, GizmoSubset.PreImageEffects);
		var postImageEffects = viewRenderData.context.CreateGizmoRendererList(viewRenderData.camera, GizmoSubset.PostImageEffects);

		using var pass = renderGraph.AddGenericRenderPass("", (preImageEffects, postImageEffects));
		pass.SetRenderFunction(static (command, pass, data) =>
		{
			command.DrawRendererList(data.preImageEffects);
			command.DrawRendererList(data.postImageEffects);
		});
	#endif
	}
}