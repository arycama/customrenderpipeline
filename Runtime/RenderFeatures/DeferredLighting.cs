using UnityEngine;
using UnityEngine.Rendering;

public class DeferredLighting : ViewRenderFeature
{
	private readonly Material material;
	private readonly Sky.Settings skySettings;

	public DeferredLighting(RenderGraph renderGraph, Sky.Settings skySettings) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Deferred Lighting")) { hideFlags = HideFlags.HideAndDontSave };
		this.skySettings = skySettings;
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		void RenderPass(int index)
		{
			using var pass = renderGraph.AddFullscreenRenderPass("Deferred Lighting");

			pass.Initialize(material, index);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());

			pass.ReadResource<DfgData>();
			pass.ReadResource<FrameData>();
			pass.ReadResource<EnvironmentData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<LightingData>();
			pass.ReadResource<ShadowData>();
			pass.ReadResource<AutoExposureData>();

			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<CameraStencil>();
			pass.ReadRtHandle<GBufferAlbedoMetallic>();
			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadRtHandle<GBufferBentNormalOcclusion>();
			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<TemporalAAData>();

			pass.ReadResource<SkyTransmittanceData>();
			pass.ReadResource<CloudShadowDataResult>();

			pass.ReadResource<ScreenSpaceShadows.Result>();
			pass.ReadResource<LightingSetup.Result>();
			pass.ReadResource<ClusteredLightCulling.Result>();
			pass.ReadResource<VolumetricLighting.Result>();

			pass.ReadResource<DiffuseGlobalIllumination.Result>();
			pass.ReadResource<ScreenSpaceReflectionResult>();

			pass.ReadResource<ParticleShadowData>();
		}

		RenderPass(0); // No translucency
		RenderPass(1); // Translucency

		//  Final pass renders background and composites the sky, clouds and volumetric lighting
		// TODO: this currently renders before the sun/moon disk meaning some pixels are overwritten. Could instead use a stencil bit to avoid
		//using (var pass = renderGraph.AddFullscreenRenderPass("Render Sky"))
		//{
		//	pass.Initialize(material, 2);
		//	pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
		//	pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.ReadOnlyDepthStencil);
		//	pass.AddRenderPassData<CloudRenderResult>();
		//	pass.AddRenderPassData<AutoExposureData>();
		//	pass.AddRenderPassData<SkyResultData>();
		//	pass.AddRenderPassData<TemporalAAData>();
		//	pass.AddRenderPassData<VolumetricLighting.Result>();

		//	pass.SetRenderFunction(static (command, pass) =>
		//	{
		//		if (skySettings.StarMap)
		//			pass.SetTexture("Stars", skySettings.StarMap);
		//	});
		//}
	}
}
