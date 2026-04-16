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
		var tempId = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);

        var camera = viewRenderData.camera;
        var focalDistance = camera.usePhysicalProperties ? camera.focusDistance : settings.FocalDistance;
        var sensorSize = (camera.usePhysicalProperties ? camera.sensorSize.y : lensSettings.SensorSize) * 0.001f;
        var aperture = camera.usePhysicalProperties ? camera.aperture : lensSettings.Aperture;
        var focalLength = PhysicalCameraUtility.FocalLength(sensorSize, viewRenderData.tanHalfFov.y);
		var apertureRadius = PhysicalCameraUtility.ApertureRadius(focalLength, aperture);

		if (settings.UseRaytracing)
		{
			// Need to set some things as globals so that hit shaders can access them..
			using (var pass = renderGraph.AddGenericRenderPass("Depth of Field Raytrace Setup"))
			{
                pass.PreventNewSubPass = true;
                pass.ReadResource<SkyReflectionAmbientData>();
				pass.ReadResource<LightingSetup.Result>();
				pass.ReadResource<AutoExposureData>();
				pass.ReadResource<AtmospherePropertiesAndTables>();
			    pass.ReadResource<TerrainFrameData>(true);
				pass.ReadResource<TerrainViewData>(true);
				pass.ReadResource<CloudShadowDataResult>();
				pass.ReadResource<ShadowData>();
				pass.ReadResource<ViewData>();
				pass.ReadResource<FrameData>();
			}

			using (var pass = renderGraph.AddRaytracingRenderPass("Depth of Field", 
			(
				focusDistance: Math.Max(camera.nearClipPlane, focalDistance),
				apertureRadius,
				settings.SampleCount,
				taaSettings.IsEnabled ? 1.0f : 0.0f
			)))
			{
				var raytracingData = renderGraph.GetResource<RaytracingResult>();

				pass.Initialize(raytracingShader, "RayGeneration", "Raytracing", raytracingData.Rtas, viewRenderData.viewSize.x, viewRenderData.viewSize.y, 1, raytracingData.Bias, raytracingData.DistantBias, viewRenderData.tanHalfFov.y);
                pass.PreventNewSubPass = true;
                pass.WriteTexture(tempId, "HitColor");
				//pass.WriteTexture(hitResult, "HitResult");

				pass.ReadResource<AtmospherePropertiesAndTables>();
				pass.ReadRtHandle<CameraDepth>();
				pass.ReadResource<ViewData>();
				pass.ReadResource<FrameData>();
			    pass.ReadResource<TerrainFrameData>(true);
				pass.ReadResource<TerrainViewData>(true);

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
				focusDistance: Math.Max(camera.nearClipPlane, focalDistance),
				apertureRadius,
				settings.SampleCount,
				taaSettings.IsEnabled ? 1.0f : 0.0f,
				viewRenderData.viewSize
			)))
			{
				pass.Initialize(material, viewRenderData.viewSize, viewRenderData.viewCount);
                pass.PreventNewSubPass = true;
				pass.WriteTexture(tempId);

				pass.ReadRtHandle<CameraTarget>();
				pass.ReadRtHandle<CameraDepth>();
				pass.ReadRtHandle<HiZMinDepth>();
				pass.ReadResource<FrameData>();
				pass.ReadResource<ViewData>();
                pass.ReadRtHandle<PreviousCameraTarget>();
                pass.ReadRtHandle<CameraVelocity>();

                pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetFloat("_FocusDistance", data.focusDistance);
					pass.SetFloat("_ApertureRadius", data.apertureRadius);
					pass.SetFloat("_SampleCount", data.SampleCount);
					pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(data.viewSize) - 1);
					pass.SetFloat("_TaaEnabled", data.Item4);
				});
			}
		}

		renderGraph.SetRTHandle<CameraTarget>(tempId);
	}
}