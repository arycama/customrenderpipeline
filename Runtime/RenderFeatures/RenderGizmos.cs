using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public class RenderGizmos : CameraRenderFeature
{
	public RenderGizmos(RenderGraph renderGraph) : base(renderGraph)
	{
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
#if UNITY_EDITOR
		if (!UnityEditor.Handles.ShouldRenderGizmos())
			return;

		var preImageEffects = context.CreateGizmoRendererList(camera, GizmoSubset.PreImageEffects);
		var postImageEffects = context.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects);

		using var pass = renderGraph.AddRenderPass<GenericRenderPass>("");
		pass.SetRenderFunction((command, pass) =>
		{
			command.DrawRendererList(preImageEffects);
			command.DrawRendererList(postImageEffects);
		});
	#endif
	}
}