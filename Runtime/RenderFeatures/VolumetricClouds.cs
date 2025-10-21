using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class VolumetricClouds : CameraRenderFeature
{
    private readonly Material material;
    private readonly Settings settings;
    private readonly PersistentRTHandleCache cloudLuminanceTextureCache, cloudTransmittanceTextureCache;
	private readonly Sky.Settings skySettings;

	public VolumetricClouds(Settings settings, RenderGraph renderGraph, Sky.Settings skySettings) : base(renderGraph)
    {
        this.settings = settings;
		this.skySettings = skySettings;
		material = new Material(Shader.Find("Hidden/Volumetric Clouds")) { hideFlags = HideFlags.HideAndDontSave };

        cloudLuminanceTextureCache = new(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Cloud Luminance", isScreenTexture: true);
        cloudTransmittanceTextureCache = new(GraphicsFormat.R16_UNorm, renderGraph, "Cloud Transmittance", isScreenTexture: true);
	}

    protected override void Cleanup(bool disposing)
    {
        cloudLuminanceTextureCache.Dispose();
        cloudTransmittanceTextureCache.Dispose();
	}

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
		renderGraph.AddProfileBeginPass("Clouds");

        var cloudLuminanceTemp = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
        var cloudTransmittanceTemp = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_UNorm, isScreenTexture: true);
        var cloudDepth = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_SFloat, isScreenTexture: true);
		var time = (float)renderGraph.GetResource<TimeData>().time;

		using (var pass = renderGraph.AddFullscreenRenderPass("Render", (settings, time)))
		{
			// Determine pass
			var keyword = string.Empty;
			var viewHeight1 = camera.transform.position.y;
			if (viewHeight1 > settings.StartHeight)
			{
				if (viewHeight1 > settings.StartHeight + settings.LayerThickness)
				{
					keyword = "ABOVE_CLOUD_LAYER";
				}
			}
			else
			{
				keyword = "BELOW_CLOUD_LAYER";
			}

			pass.Initialize(material, 4, 1, keyword);
			pass.WriteTexture(cloudLuminanceTemp, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(cloudTransmittanceTemp, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(cloudDepth, RenderBufferLoadAction.DontCare);

			pass.AddRenderPassData<CloudData>();
			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<CloudShadowDataResult>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<LightingData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<SkyTransmittanceData>();
			pass.ReadRtHandle<CameraDepth>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				data.settings.SetCloudPassData(pass, data.time);
			});
		}

        // Reprojection+output
        var (luminanceCurrent, luminanceHistory, luminanceWasCreated) = cloudLuminanceTextureCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
        var (transmittanceCurrent, transmittanceHistory, transmittanceWasCreated) = cloudTransmittanceTextureCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
		using (var pass = renderGraph.AddFullscreenRenderPass("Temporal", (luminanceWasCreated, settings.StationaryBlend, settings.MotionBlend, settings.MotionFactor, settings.DepthThreshold
			, luminanceHistory, transmittanceHistory, camera.ScaledViewSize(), skySettings, settings, time)))
		{
			pass.Initialize(material, 5);
			pass.WriteTexture(luminanceCurrent, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(transmittanceCurrent, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.ReadTexture("_Input", cloudLuminanceTemp);
			pass.ReadTexture("_InputTransmittance", cloudTransmittanceTemp);
			pass.ReadTexture("_History", luminanceHistory);
			pass.ReadTexture("_TransmittanceHistory", transmittanceHistory);
			pass.ReadTexture("CloudDepthTexture", cloudDepth);

			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<ViewData>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<PreviousCameraDepth>();
			pass.ReadRtHandle<PreviousCameraVelocity>();
			pass.ReadRtHandle<CameraVelocity>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_IsFirst", data.luminanceWasCreated ? 1.0f : 0.0f);
				pass.SetFloat("_StationaryBlend", data.StationaryBlend);
				pass.SetFloat("_MotionBlend", data.MotionBlend);
				pass.SetFloat("_MotionFactor", data.MotionFactor);
				pass.SetFloat("DepthThreshold", data.DepthThreshold);

				pass.SetVector("_HistoryScaleLimit", pass.GetScaleLimit2D(data.luminanceHistory));
				pass.SetVector("_TransmittanceHistoryScaleLimit", pass.GetScaleLimit2D(data.transmittanceHistory));

				pass.SetInt("_MaxWidth", data.Item8.x - 1);
				pass.SetInt("_MaxHeight", data.Item8.y - 1);

				if (data.skySettings.StarMap != null)
					pass.SetTexture("Stars", data.skySettings.StarMap);

				pass.SetFloat("StarExposure", data.skySettings.StarExposure);

				data.settings.SetCloudPassData(pass, data.time);
			});
		}

        renderGraph.SetResource(new CloudRenderResult(luminanceCurrent, transmittanceCurrent, cloudDepth));

		renderGraph.AddProfileEndPass("Clouds");
    }
}