using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class VolumetricClouds : CameraRenderFeature
{
	private static readonly int StarsId = Shader.PropertyToID("Stars");

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
			pass.Initialize(material, 4, 1);

			// Determine pass
			var keyword = string.Empty;
			var viewHeight1 = camera.transform.position.y;
			if (viewHeight1 > settings.StartHeight)
			{
				if (viewHeight1 > settings.StartHeight + settings.LayerThickness)
				{
					pass.AddKeyword("ABOVE_CLOUD_LAYER");
				}
			}
			else
			{
				pass.AddKeyword("BELOW_CLOUD_LAYER");
			}

			pass.WriteTexture(cloudLuminanceTemp, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(cloudTransmittanceTemp, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(cloudDepth, RenderBufferLoadAction.DontCare);

			pass.ReadResource<CloudData>();
			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<CloudShadowDataResult>();
			pass.ReadResource<AutoExposureData>();
			pass.ReadResource<LightingData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<SkyTransmittanceData>();
			pass.ReadRtHandle<CameraDepth>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				data.settings.SetCloudPassData(pass, data.time);
			});
		}

		bool luminanceWasCreated = default, transmittanceWasCreated = default;
		ResourceHandle<RenderTexture> luminanceCurrent, transmittanceCurrent, luminanceHistory = default, transmittanceHistory = default;

        // Reprojection+output
		using (var pass = renderGraph.AddFullscreenRenderPass("Temporal", new VolumetricCloudTemporalData
		(
			luminanceWasCreated,
			settings.StationaryBlend,
			settings.MotionBlend,
			settings.MotionFactor,
			settings.DepthThreshold,
			luminanceHistory, 
			transmittanceHistory,
			camera.ScaledViewSize(),
			skySettings,
			settings,
			time
		)))
		{
			(luminanceCurrent, luminanceHistory, luminanceWasCreated) = cloudLuminanceTextureCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, pass.Index, camera);
			(transmittanceCurrent, transmittanceHistory, transmittanceWasCreated) = cloudTransmittanceTextureCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, pass.Index, camera);

			pass.renderData.luminanceWasCreated = luminanceWasCreated;
			pass.renderData.luminanceHistory = luminanceHistory;
			pass.renderData.transmittanceHistory = transmittanceHistory;

			pass.Initialize(material, 5);
			pass.WriteTexture(luminanceCurrent, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(transmittanceCurrent, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.ReadTexture("_Input", cloudLuminanceTemp);
			pass.ReadTexture("_InputTransmittance", cloudTransmittanceTemp);
			pass.ReadTexture("_History", luminanceHistory);
			pass.ReadTexture("_TransmittanceHistory", transmittanceHistory);
			pass.ReadTexture("CloudDepthTexture", cloudDepth);

			pass.ReadResource<TemporalAAData>();
			pass.ReadResource<AutoExposureData>();
			pass.ReadResource<ViewData>();
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

				pass.SetVector("_HistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.luminanceHistory));
				pass.SetVector("_TransmittanceHistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.transmittanceHistory));

				pass.SetInt("_MaxWidth", data.Item8.x - 1);
				pass.SetInt("_MaxHeight", data.Item8.y - 1);

				if (data.skySettings.StarMap != null)
					pass.SetTexture(StarsId, data.skySettings.StarMap);

				pass.SetFloat("StarExposure", data.skySettings.StarExposure);

				data.settings.SetCloudPassData(pass, data.time);
			});
		}

        renderGraph.SetResource(new CloudRenderResult(luminanceCurrent, transmittanceCurrent, cloudDepth));

		renderGraph.AddProfileEndPass("Clouds");
    }
}

internal struct VolumetricCloudTemporalData
{
	public bool luminanceWasCreated;
	public float StationaryBlend;
	public float MotionBlend;
	public float MotionFactor;
	public float DepthThreshold;
	public ResourceHandle<RenderTexture> luminanceHistory;
	public ResourceHandle<RenderTexture> transmittanceHistory;
	public Int2 Item8;
	public Sky.Settings skySettings;
	public VolumetricClouds.Settings settings;
	public float time;

	public VolumetricCloudTemporalData(bool luminanceWasCreated, float stationaryBlend, float motionBlend, float motionFactor, float depthThreshold, ResourceHandle<RenderTexture> luminanceHistory, ResourceHandle<RenderTexture> transmittanceHistory, Int2 item8, Sky.Settings skySettings, VolumetricClouds.Settings settings, float time)
	{
		this.luminanceWasCreated = luminanceWasCreated;
		StationaryBlend = stationaryBlend;
		MotionBlend = motionBlend;
		MotionFactor = motionFactor;
		DepthThreshold = depthThreshold;
		this.luminanceHistory = luminanceHistory;
		this.transmittanceHistory = transmittanceHistory;
		Item8 = item8;
		this.skySettings = skySettings;
		this.settings = settings;
		this.time = time;
	}

	public override bool Equals(object obj) => obj is VolumetricCloudTemporalData other && luminanceWasCreated == other.luminanceWasCreated && StationaryBlend == other.StationaryBlend && MotionBlend == other.MotionBlend && MotionFactor == other.MotionFactor && DepthThreshold == other.DepthThreshold && EqualityComparer<ResourceHandle<RenderTexture>>.Default.Equals(luminanceHistory, other.luminanceHistory) && EqualityComparer<ResourceHandle<RenderTexture>>.Default.Equals(transmittanceHistory, other.transmittanceHistory) && EqualityComparer<Int2>.Default.Equals(Item8, other.Item8) && EqualityComparer<Sky.Settings>.Default.Equals(skySettings, other.skySettings) && EqualityComparer<VolumetricClouds.Settings>.Default.Equals(settings, other.settings) && time == other.time;

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(luminanceWasCreated);
		hash.Add(StationaryBlend);
		hash.Add(MotionBlend);
		hash.Add(MotionFactor);
		hash.Add(DepthThreshold);
		hash.Add(luminanceHistory);
		hash.Add(transmittanceHistory);
		hash.Add(Item8);
		hash.Add(skySettings);
		hash.Add(settings);
		hash.Add(time);
		return hash.ToHashCode();
	}

	public void Deconstruct(out bool luminanceWasCreated, out float stationaryBlend, out float motionBlend, out float motionFactor, out float depthThreshold, out ResourceHandle<RenderTexture> luminanceHistory, out ResourceHandle<RenderTexture> transmittanceHistory, out Int2 item8, out Sky.Settings skySettings, out VolumetricClouds.Settings settings, out float time)
	{
		luminanceWasCreated = this.luminanceWasCreated;
		stationaryBlend = StationaryBlend;
		motionBlend = MotionBlend;
		motionFactor = MotionFactor;
		depthThreshold = DepthThreshold;
		luminanceHistory = this.luminanceHistory;
		transmittanceHistory = this.transmittanceHistory;
		item8 = Item8;
		skySettings = this.skySettings;
		settings = this.settings;
		time = this.time;
	}

	public static implicit operator (bool luminanceWasCreated, float StationaryBlend, float MotionBlend, float MotionFactor, float DepthThreshold, ResourceHandle<RenderTexture> luminanceHistory, ResourceHandle<RenderTexture> transmittanceHistory, Int2, Sky.Settings skySettings, VolumetricClouds.Settings settings, float time)(VolumetricCloudTemporalData value) => (value.luminanceWasCreated, value.StationaryBlend, value.MotionBlend, value.MotionFactor, value.DepthThreshold, value.luminanceHistory, value.transmittanceHistory, value.Item8, value.skySettings, value.settings, value.time);
	public static implicit operator VolumetricCloudTemporalData((bool luminanceWasCreated, float StationaryBlend, float MotionBlend, float MotionFactor, float DepthThreshold, ResourceHandle<RenderTexture> luminanceHistory, ResourceHandle<RenderTexture> transmittanceHistory, Int2, Sky.Settings skySettings, VolumetricClouds.Settings settings, float time) value) => new VolumetricCloudTemporalData(value.luminanceWasCreated, value.StationaryBlend, value.MotionBlend, value.MotionFactor, value.DepthThreshold, value.luminanceHistory, value.transmittanceHistory, value.Item8, value.skySettings, value.settings, value.time);
}