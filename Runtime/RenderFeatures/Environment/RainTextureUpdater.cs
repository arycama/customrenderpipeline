using System;
using UnityEngine;
using UnityEngine.Rendering;

public class RainTextureUpdater : CameraRenderFeature
{
	private readonly Rain.Settings settings;
	private readonly Material material, compositeMaterial;

	public RainTextureUpdater(RenderGraph renderGraph, Rain.Settings settings) : base(renderGraph)
	{
		this.settings = settings;
		material = new Material(Shader.Find("Hidden/Rain Texture")) { hideFlags = HideFlags.HideAndDontSave };
		compositeMaterial = new Material(Shader.Find("Hidden/Rain Composite")) { hideFlags = HideFlags.HideAndDontSave };
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var rainTexture = renderGraph.GetTexture(settings.Resolution, settings.Resolution, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_SNorm, isExactSize: true, hasMips: true, autoGenerateMips: true);

		using (var pass = renderGraph.AddFullscreenRenderPass("Rain Texture", (settings.Resolution, settings.Size)))
		{
			pass.Initialize(material);

			pass.WriteTexture(rainTexture);

			pass.ReadResource<FrameData>();
			pass.ReadResource<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("Resolution", data.Resolution);
				pass.SetFloat("Size", data.Size);
			});

			renderGraph.SetResource(new RainTextureResult(rainTexture, settings.Size));
		}

		var albedoMetallicCopy = renderGraph.GetTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
		var normalRoughnessCopy = renderGraph.GetTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
		var bentNormalOcclusionCopy = renderGraph.GetTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());

		using (var pass = renderGraph.AddFullscreenRenderPass("Composite", (albedoMetallicCopy, normalRoughnessCopy, bentNormalOcclusionCopy, settings.WetLevel)))
		{
			pass.Initialize(compositeMaterial);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(albedoMetallicCopy);
			pass.WriteTexture(normalRoughnessCopy);
			pass.WriteTexture(bentNormalOcclusionCopy);

			pass.ReadRtHandle<GBufferAlbedoMetallic>();
			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadRtHandle<GBufferBentNormalOcclusion>();

			pass.ReadRtHandle<CameraTarget>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<CameraStencil>();
			pass.ReadResource<RainTextureResult>();

			renderGraph.SetRTHandle<GBufferAlbedoMetallic>(albedoMetallicCopy);
			renderGraph.SetRTHandle<GBufferNormalRoughness>(normalRoughnessCopy);
			renderGraph.SetRTHandle<GBufferBentNormalOcclusion>(bentNormalOcclusionCopy);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("WetLevel", data.WetLevel);
			});
		}
	}
}
