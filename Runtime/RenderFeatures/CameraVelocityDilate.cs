using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class CameraVelocityDilate : ViewRenderFeature
{
	private readonly Material material;
    private readonly TemporalAA.Settings setings;

	public CameraVelocityDilate(RenderGraph renderGraph, TemporalAA.Settings setings) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
        this.setings = setings;
	}

	public override void Render(ViewRenderData viewRenderData)
    {
        if (!setings.IsEnabled)
            return;

		var result = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R16G16_SFloat);
		using (var pass = renderGraph.AddFullscreenRenderPass("Velocity Dilate"))
		{
			pass.Initialize(material, 1);
			pass.WriteTexture(result);
			pass.ReadRtHandle<CameraVelocity>();
			pass.ReadRtHandle<CameraDepth>();
		}

		renderGraph.SetRTHandle<CameraVelocity>(result);
	}
}