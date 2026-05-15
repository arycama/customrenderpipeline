using UnityEngine;
using UnityEngine.Rendering;

public class GenerateCameraVelocity : ViewRenderFeature
{
	private readonly Material material;

	public GenerateCameraVelocity(RenderGraph renderGraph) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		using (var pass = renderGraph.AddFullscreenRenderPass("Camera Velocity"))
		{
			pass.Initialize(material, viewRenderData.viewSize, viewRenderData.viewCount, isScreenPass: true);
            pass.WriteRtHandle<CameraVelocity>();
			pass.WriteRtHandleDepth<CameraDepth>(SubPassFlags.ReadOnlyDepthStencil);
			pass.ReadResource<ViewData>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadResource<TemporalAAData>();
		}
	}
}
