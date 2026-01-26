using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class DepthOfField : ViewRenderFeature
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

	public override void Render(ViewRenderData viewRenderData)
    {
		if (!settings.IsEnabled)
			return;

		var computeShader = Resources.Load<ComputeShader>("PostProcessing/DepthOfField");
		var tempId = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.B10G11R11_UFloatPack32);
		var sensorSize = lensSettings.SensorSize * 0.001f; // Convert from mm to m
		var focalLength = 0.5f * sensorSize / viewRenderData.tanHalfFov.y;
		var apertureRadius = 0.5f * focalLength / lensSettings.Aperture;

		if (settings.UseRaytracing)
		{
			// Need to set some things as globals so that hit shaders can access them..
			using (var pass = renderGraph.AddGenericRenderPass("Depth of Field Raytrace Setup"))
			{
				pass.ReadResource<SkyReflectionAmbientData>();
				pass.ReadResource<LightingSetup.Result>();
				pass.ReadResource<AutoExposureData>();
				pass.ReadResource<AtmospherePropertiesAndTables>();
				pass.ReadResource<TerrainRenderData>(true);
				pass.ReadResource<CloudShadowDataResult>();
				pass.ReadResource<ShadowData>();
				pass.ReadResource<ViewData>();
				pass.ReadResource<FrameData>();
			}

			using (var pass = renderGraph.AddRaytracingRenderPass("Depth of Field", 
			(
				focusDistance: lensSettings.FocalDistance,
				apertureRadius,
				settings.SampleCount,
				taaSettings.IsEnabled ? 1.0f : 0.0f
			)))
			{
				var raytracingData = renderGraph.GetResource<RaytracingResult>();

				pass.Initialize(raytracingShader, "RayGeneration", "Raytracing", raytracingData.Rtas, viewRenderData.viewSize.x, viewRenderData.viewSize.y, 1, raytracingData.Bias, raytracingData.DistantBias, viewRenderData.tanHalfFov.y);
				pass.WriteTexture(tempId, "HitColor");
				//pass.WriteTexture(hitResult, "HitResult");

				pass.ReadResource<AtmospherePropertiesAndTables>();
				pass.ReadRtHandle<CameraDepth>();
				pass.ReadResource<ViewData>();
				pass.ReadResource<FrameData>();
				pass.ReadResource<TerrainRenderData>(true);

				pass.SetRenderFunction(static (command, pass, data) =>
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
			using (var pass = renderGraph.AddFullscreenRenderPass("Depth of Field", 
			(
				focusDistance: lensSettings.FocalDistance,
				apertureRadius,
				settings.SampleCount,
				taaSettings.IsEnabled ? 1.0f : 0.0f,
				viewRenderData.viewSize
			)))
			{
				pass.Initialize(material);
				pass.WriteTexture(tempId);

				pass.ReadRtHandle<CameraTarget>();
				pass.ReadRtHandle<CameraDepth>();
				pass.ReadRtHandle<HiZMinDepth>();
				pass.ReadResource<FrameData>();
				pass.ReadResource<ViewData>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetFloat("_FocusDistance", data.focusDistance);
					pass.SetFloat("_ApertureRadius", data.apertureRadius);
					pass.SetFloat("_SampleCount", data.SampleCount);
					pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(data.Item5) - 1);
					pass.SetFloat("_TaaEnabled", data.Item4);
				});
			}
		}

		renderGraph.SetRTHandle<CameraTarget>(tempId);
	}
}