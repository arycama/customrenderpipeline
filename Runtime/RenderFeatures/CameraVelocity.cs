using UnityEngine;
using UnityEngine.Rendering;

public class CameraVelocity : CameraRenderFeature
{
	private readonly Material material;

	public CameraVelocity(RenderGraph renderGraph) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Camera Velocity"))
		{
			pass.Initialize(material);
			pass.WriteTexture(renderGraph.GetResource<VelocityData>().Handle);
			pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<CameraDepthData>();
			pass.AddRenderPassData<TemporalAAData>();
		}
	}
}
