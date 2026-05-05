using UnityEngine;
using UnityEngine.Experimental.Rendering;
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
        var gizmosTarget = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

		using var pass = renderGraph.AddGenericRenderPass("Render Gizmos", (preImageEffects, postImageEffects, gizmosTarget));
        pass.WriteTexture(gizmosTarget);

		pass.SetRenderFunction(static (command, pass, data) =>
		{
            command.SetRenderTarget(pass.GetRenderTexture(data.gizmosTarget));
            command.ClearRenderTarget(RTClearFlags.Color, Color.clear);
			command.DrawRendererList(data.preImageEffects);
			command.DrawRendererList(data.postImageEffects);
		});

        renderGraph.SetRTHandle<GizmosTarget>(gizmosTarget);
	#endif
	}
}