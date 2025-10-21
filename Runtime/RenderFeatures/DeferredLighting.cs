using UnityEngine;
using UnityEngine.Rendering;

public class DeferredLighting : CameraRenderFeature
{
	private readonly Material material;
	private readonly Sky.Settings skySettings;

	public DeferredLighting(RenderGraph renderGraph, Sky.Settings skySettings) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
		this.skySettings = skySettings;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		void RenderPass(int index)
		{
			using var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Deferred Lighting");

			pass.Initialize(material, index);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>().handle, RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>().handle);

			pass.AddRenderPassData<DfgData>();
			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<EnvironmentData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<LightingData>();
			pass.AddRenderPassData<ShadowData>();
			pass.AddRenderPassData<AutoExposureData>();

			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<CameraStencil>();
			pass.ReadRtHandle<GBufferAlbedoMetallic>();
			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadRtHandle<GBufferBentNormalOcclusion>();
			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<TemporalAAData>();

			pass.AddRenderPassData<SkyTransmittanceData>();
			pass.AddRenderPassData<CloudShadowDataResult>();

			pass.AddRenderPassData<ScreenSpaceShadows.Result>();
			pass.AddRenderPassData<LightingSetup.Result>();
			pass.AddRenderPassData<ClusteredLightCulling.Result>();
			pass.AddRenderPassData<VolumetricLighting.Result>();

			pass.AddRenderPassData<DiffuseGlobalIllumination.Result>();
			pass.AddRenderPassData<ScreenSpaceReflectionResult>();

			pass.AddRenderPassData<ParticleShadowData>();
		}

		RenderPass(0); // No translucency
		RenderPass(1); // Translucency

		//  Final pass renders background and composites the sky, clouds and volumetric lighting
		// TODO: this currently renders before the sun/moon disk meaning some pixels are overwritten. Could instead use a stencil bit to avoid
		//using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Render Sky"))
		//{
		//	pass.Initialize(material, 2);
		//	pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
		//	pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepthStencil);
		//	pass.AddRenderPassData<CloudRenderResult>();
		//	pass.AddRenderPassData<AutoExposureData>();
		//	pass.AddRenderPassData<SkyResultData>();
		//	pass.AddRenderPassData<TemporalAAData>();
		//	pass.AddRenderPassData<VolumetricLighting.Result>();

		//	pass.SetRenderFunction((command, pass) =>
		//	{
		//		if (skySettings.StarMap)
		//			pass.SetTexture("Stars", skySettings.StarMap);
		//	});
		//}
	}
}
