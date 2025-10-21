using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class ScreenSpaceShadows : CameraRenderFeature
{
	private readonly Material material;
	private readonly Settings settings;
	private readonly LightingSettings lightingSettings;
	private readonly PersistentRTHandleCache temporalCache;
	private readonly RayTracingShader shadowRaytracingShader;

	public ScreenSpaceShadows(RenderGraph renderGraph, Settings settings, LightingSettings lightingSettings) : base(renderGraph)
	{
		this.settings = settings;
		this.lightingSettings = lightingSettings;
		material = new Material(Shader.Find("Hidden/Screen Space Shadows")) { hideFlags = HideFlags.HideAndDontSave };
		shadowRaytracingShader = Resources.Load<RayTracingShader>("Raytracing/Shadow");
		temporalCache = new PersistentRTHandleCache(GraphicsFormat.R16_UNorm, renderGraph, "Screen Space Shadows", isScreenTexture: true);
	}

	protected override void Cleanup(bool disposing)
	{
		temporalCache.Dispose();
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		using var scope = renderGraph.AddProfileScope("Screen Space Shadows");

		var tempResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);

		if (settings.UseRaytracing)
		{
			using var pass = renderGraph.AddRaytracingRenderPass("Raytraced Shadows");

			var raytracingData = renderGraph.GetResource<RaytracingResult>();
			pass.Initialize(shadowRaytracingShader, "RayGeneration", "RaytracingVisibility", raytracingData.Rtas, camera.scaledPixelWidth, camera.scaledPixelHeight, 1, raytracingData.Bias, raytracingData.DistantBias, camera.TanHalfFov());

			pass.WriteTexture(tempResult, "HitResult");

			pass.ReadRtHandle<CameraDepth>();
			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<LightingData>();
			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.AddRenderPassData<ViewData>();

			// Only here to avoid memory leaks due to it not being used..
			pass.ReadRtHandle<HiZMinDepth>();
		}
		else
		{
			using var pass = renderGraph.AddFullscreenRenderPass("Screen Space Shadows", (settings.MaxSamples, settings.Thickness, settings.Intensity, camera.ScaledViewSize()));
			pass.Initialize(material, 0, 1);

			pass.WriteTexture(tempResult, RenderBufferLoadAction.DontCare);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);

			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<HiZMinDepth>();
			pass.AddRenderPassData<LightingData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<FrameData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_MaxSteps", data.MaxSamples);
				pass.SetFloat("_Thickness", data.Thickness);
				pass.SetFloat("_Intensity", data.Intensity);
				pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(data.Item4) - 1);
			});
		}

		var spatialResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_UNorm, isScreenTexture: true);
		using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Shadows Spatial", (settings.Intensity, settings.MaxSamples, settings.Thickness, settings.ResolveSamples, settings.ResolveSize)))
		{
			pass.Initialize(material, 1);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);

			pass.ReadTexture("_Input", tempResult);

			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<SkyReflectionAmbientData>();
			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<ViewData>();
			pass.ReadRtHandle<CameraVelocity>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<CameraStencil>();
			pass.AddRenderPassData<LightingData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_Intensity", data.Intensity);
				pass.SetFloat("_MaxSteps", data.MaxSamples);
				pass.SetFloat("_Thickness", data.Thickness);
				pass.SetInt("_ResolveSamples", data.ResolveSamples);
				pass.SetFloat("_ResolveSize", data.ResolveSize);
				pass.SetFloat("DiffuseGiStrength", data.Intensity);
			});
		}

		// Write final temporal result out to rgba16 (color+weight) and rgb111110 for final ambient composition
		var (current, history, wasCreated) = temporalCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
		using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Shadows Temporal", (wasCreated, history, settings.Intensity, settings.MaxSamples, settings.Thickness)))
		{
			pass.Initialize(material, 2);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

			pass.ReadTexture("_TemporalInput", spatialResult);
			pass.ReadTexture("_History", history);
			//pass.ReadTexture("_HitResult", hitResult);
			//pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
			//pass.ReadTexture("RayDepth", rayDepth);

			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<SkyReflectionAmbientData>();
			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<ViewData>();
			pass.ReadRtHandle<CameraVelocity>();
			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<CameraStencil>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_IsFirst", data.wasCreated ? 1.0f : 0.0f);
				pass.SetVector("_HistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.history));
				pass.SetFloat("_Intensity", data.Intensity);
				pass.SetFloat("_MaxSteps", data.MaxSamples);
				pass.SetFloat("_Thickness", data.Thickness);
			});
		}

		renderGraph.SetResource(new Result(current, settings.Intensity));
	}
}