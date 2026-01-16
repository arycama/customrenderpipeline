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
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
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
	}
}
