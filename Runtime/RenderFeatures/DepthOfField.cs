using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class DepthOfField : CameraRenderFeature
{
	private readonly Settings settings;
	private readonly LensSettings lensSettings;
	private readonly Material material;
	private readonly RayTracingShader raytracingShader;
	private TemporalAA.Settings taaSettings;

	public DepthOfField(Settings settings, LensSettings lensSettings, RenderGraph renderGraph, TemporalAA.Settings taaSettings) : base(renderGraph)
	{
		this.settings = settings;
		this.lensSettings = lensSettings;
		this.taaSettings = taaSettings;

		material = new Material(Shader.Find("Hidden/Depth of Field")) { hideFlags = HideFlags.HideAndDontSave };
		raytracingShader = Resources.Load<RayTracingShader>("Raytracing/DepthOfField");
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		if (!settings.IsEnabled)
			return;

		var computeShader = Resources.Load<ComputeShader>("PostProcessing/DepthOfField");
		var viewData = renderGraph.GetResource<ViewData>();

		var tempId = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.B10G11R11_UFloatPack32);
		var sensorSize = lensSettings.SensorSize * 0.001f; // Convert from mm to m
		var focalLength = 0.5f * sensorSize / camera.TanHalfFov();
		var apertureRadius = 0.5f * focalLength / lensSettings.Aperture;

		if (settings.UseRaytracing)
		{
			// Need to set some things as globals so that hit shaders can access them..
			using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Depth of Field Raytrace Setup"))
			{
				pass.AddRenderPassData<SkyReflectionAmbientData>();
				pass.AddRenderPassData<LightingSetup.Result>();
				pass.AddRenderPassData<AutoExposureData>();
				pass.AddRenderPassData<AtmospherePropertiesAndTables>();
				pass.AddRenderPassData<TerrainRenderData>(true);
				pass.AddRenderPassData<CloudShadowDataResult>();
				pass.AddRenderPassData<ShadowData>();
				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();
			}

			using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Depth of Field "))
			{
				var raytracingData = renderGraph.GetResource<RaytracingResult>();

				pass.Initialize(raytracingShader, "RayGeneration", "Raytracing", raytracingData.Rtas, camera.scaledPixelWidth, camera.scaledPixelHeight, 1, raytracingData.Bias, raytracingData.DistantBias, camera.TanHalfFov());
				pass.WriteTexture(tempId, "HitColor");
				//pass.WriteTexture(hitResult, "HitResult");

				pass.AddRenderPassData<AtmospherePropertiesAndTables>();
				pass.AddRenderPassData<CameraDepthData>();
				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();
				pass.AddRenderPassData<TerrainRenderData>(true);

				pass.SetRenderFunction((
					focusDistance: lensSettings.FocalDistance,
					apertureRadius,
					settings.SampleCount,
					taaSettings.IsEnabled ? 1.0f : 0.0f
				),
				(command, pass, data) =>
				{
					pass.SetFloat("_FocusDistance", data.focusDistance);
					pass.SetFloat("_ApertureRadius", data.apertureRadius);
					pass.SetFloat("_SampleCount", data.SampleCount);
					pass.SetFloat("_TaaEnabled", data.Item4);
				});
			}
		}
		else
		{
			using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Depth of Field"))
			{
				pass.Initialize(material);
				pass.WriteTexture(tempId, RenderBufferLoadAction.DontCare);

				pass.AddRenderPassData<CameraTargetData>();
				pass.AddRenderPassData<CameraDepthData>();
				pass.AddRenderPassData<HiZMinDepthData>();
				pass.AddRenderPassData<FrameData>();
				pass.AddRenderPassData<ViewData>();

				pass.SetRenderFunction((
					focusDistance: lensSettings.FocalDistance,
					apertureRadius,
					settings.SampleCount,
					taaSettings.IsEnabled ? 1.0f : 0.0f
				),
				(command, pass, data) =>
				{
					pass.SetFloat("_FocusDistance", data.focusDistance);
					pass.SetFloat("_ApertureRadius", data.apertureRadius);
					pass.SetFloat("_SampleCount", data.SampleCount);
					pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(camera.scaledPixelWidth, camera.scaledPixelHeight) - 1);
					pass.SetFloat("_TaaEnabled", data.Item4);
				});
			}
		}

		renderGraph.SetResource(new CameraTargetData(tempId));
	}
}