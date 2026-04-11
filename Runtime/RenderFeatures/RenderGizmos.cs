using UnityEngine;
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
        var worldToClip = Matrix4x4.identity;// GL.GetGPUProjectionMatrix(viewRenderData.camera.projectionMatrix * viewRenderData.camera.worldToCameraMatrix, false);

		using var pass = renderGraph.AddGenericRenderPass("Render Gizmos", (preImageEffects, postImageEffects, worldToClip));
		pass.SetRenderFunction(static (command, pass, data) =>
		{
            command.SetGlobalMatrix("unity_MatrixVP", data.worldToClip);
			command.DrawRendererList(data.preImageEffects);
			command.DrawRendererList(data.postImageEffects);
		});
	#endif
	}
}