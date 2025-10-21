using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class CameraVelocityDilate : CameraRenderFeature
{
	private readonly Material material;

	public CameraVelocityDilate(RenderGraph renderGraph) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Camera Motion Vectors")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var result = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R16G16_SFloat);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Velocity Dilate"))
		{
			pass.Initialize(material, 1);
			pass.WriteTexture(result, RenderBufferLoadAction.DontCare);
			pass.ReadRtHandle<VelocityData>();
			pass.ReadRtHandle<CameraDepth>();
		}

		renderGraph.SetRTHandle<VelocityData>(result);
	}
}