using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public partial class Sky : CameraRenderFeature
{
	private readonly Settings settings;
	private readonly Material skyMaterial;

	private readonly PersistentRTHandleCache textureCache;

	public Sky(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		this.settings = settings;

		skyMaterial = new Material(Shader.Find("Hidden/Physical Sky")) { hideFlags = HideFlags.HideAndDontSave };
		textureCache = new(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Physical Sky", isScreenTexture: true);
	}

	protected override void Cleanup(bool disposing)
	{
		Object.DestroyImmediate(skyMaterial);
		textureCache.Dispose();
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		renderGraph.AddProfileBeginPass("Sky");

		var skyTemp = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
		using (var pass = renderGraph.AddFullscreenRenderPass("Render Sky", settings.RenderSamples))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Render Sky"));
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(skyTemp, RenderBufferLoadAction.DontCare);

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<CloudRenderResult>();
			pass.AddRenderPassData<CloudShadowDataResult>();
			pass.AddRenderPassData<LightingData>();
			pass.AddRenderPassData<ShadowData>();
			pass.AddRenderPassData<SkyReflectionAmbientData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<SkyTransmittanceData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_Samples", data);
			});
		}

		using (var pass = renderGraph.AddFullscreenRenderPass("Render Scene", settings.RenderSamples))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Render Scene"));
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(skyTemp, RenderBufferLoadAction.Load);

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<CloudRenderResult>();
			pass.AddRenderPassData<CloudShadowDataResult>();
			pass.AddRenderPassData<LightingData>();
			pass.AddRenderPassData<ShadowData>();
			pass.AddRenderPassData<SkyReflectionAmbientData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<SkyTransmittanceData>();
			pass.ReadRtHandle<CameraDepth>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("_Samples", data);
			});
		}

		// Reprojection
		var (current, history, wasCreated) = textureCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
		using (var pass = renderGraph.AddFullscreenRenderPass("Temporal", new SkyTemporalData(history, wasCreated, settings.StationaryBlend, settings.MotionBlend, settings.MotionFactor, settings.DepthFactor, settings.ClampWindow, settings.MaxFrameCount, camera.ScaledViewSize())))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Temporal"));
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
			pass.ReadTexture("_SkyInput", skyTemp);
			pass.ReadTexture("_SkyHistory", history);

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<CloudRenderResult>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.ReadRtHandle<PreviousCameraDepth>();
			pass.ReadRtHandle<PreviousCameraVelocity>();
			pass.AddRenderPassData<ViewData>();
			pass.ReadRtHandle<CameraVelocity>();
			pass.ReadRtHandle<CameraDepth>();
			pass.AddRenderPassData<VolumetricLighting.Result>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetVector("_SkyHistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.history));

				pass.SetFloat("_IsFirst", data.wasCreated ? 1.0f : 0.0f);
				pass.SetFloat("_StationaryBlend", data.StationaryBlend);
				pass.SetFloat("_MotionBlend", data.MotionBlend);
				pass.SetFloat("_MotionFactor", data.MotionFactor);
				pass.SetFloat("_DepthFactor", data.DepthFactor);
				pass.SetFloat("_ClampWindow", data.ClampWindow);

				pass.SetFloat("_MaxFrameCount", data.MaxFrameCount);

				pass.SetInt("_MaxWidth", data.Item9.x - 1);
				pass.SetInt("_MaxHeight", data.Item9.y - 1);
			});
		}

		renderGraph.AddProfileEndPass("Sky");
	}
}

internal struct SkyTemporalData
{
	public ResourceHandle<RenderTexture> history;
	public bool wasCreated;
	public float StationaryBlend;
	public float MotionBlend;
	public float MotionFactor;
	public float DepthFactor;
	public float ClampWindow;
	public int MaxFrameCount;
	public Int2 Item9;

	public SkyTemporalData(ResourceHandle<RenderTexture> history, bool wasCreated, float stationaryBlend, float motionBlend, float motionFactor, float depthFactor, float clampWindow, int maxFrameCount, Int2 item9)
	{
		this.history = history;
		this.wasCreated = wasCreated;
		StationaryBlend = stationaryBlend;
		MotionBlend = motionBlend;
		MotionFactor = motionFactor;
		DepthFactor = depthFactor;
		ClampWindow = clampWindow;
		MaxFrameCount = maxFrameCount;
		Item9 = item9;
	}

	public override bool Equals(object obj) => obj is SkyTemporalData other && EqualityComparer<ResourceHandle<RenderTexture>>.Default.Equals(history, other.history) && wasCreated == other.wasCreated && StationaryBlend == other.StationaryBlend && MotionBlend == other.MotionBlend && MotionFactor == other.MotionFactor && DepthFactor == other.DepthFactor && ClampWindow == other.ClampWindow && MaxFrameCount == other.MaxFrameCount && EqualityComparer<Int2>.Default.Equals(Item9, other.Item9);

	public override int GetHashCode()
	{
		var hash = new System.HashCode();
		hash.Add(history);
		hash.Add(wasCreated);
		hash.Add(StationaryBlend);
		hash.Add(MotionBlend);
		hash.Add(MotionFactor);
		hash.Add(DepthFactor);
		hash.Add(ClampWindow);
		hash.Add(MaxFrameCount);
		hash.Add(Item9);
		return hash.ToHashCode();
	}

	public void Deconstruct(out ResourceHandle<RenderTexture> history, out bool wasCreated, out float stationaryBlend, out float motionBlend, out float motionFactor, out float depthFactor, out float clampWindow, out int maxFrameCount, out Int2 item9)
	{
		history = this.history;
		wasCreated = this.wasCreated;
		stationaryBlend = StationaryBlend;
		motionBlend = MotionBlend;
		motionFactor = MotionFactor;
		depthFactor = DepthFactor;
		clampWindow = ClampWindow;
		maxFrameCount = MaxFrameCount;
		item9 = Item9;
	}

	public static implicit operator (ResourceHandle<RenderTexture> history, bool wasCreated, float StationaryBlend, float MotionBlend, float MotionFactor, float DepthFactor, float ClampWindow, int MaxFrameCount, Int2)(SkyTemporalData value) => (value.history, value.wasCreated, value.StationaryBlend, value.MotionBlend, value.MotionFactor, value.DepthFactor, value.ClampWindow, value.MaxFrameCount, value.Item9);
	public static implicit operator SkyTemporalData((ResourceHandle<RenderTexture> history, bool wasCreated, float StationaryBlend, float MotionBlend, float MotionFactor, float DepthFactor, float ClampWindow, int MaxFrameCount, Int2) value) => new SkyTemporalData(value.history, value.wasCreated, value.StationaryBlend, value.MotionBlend, value.MotionFactor, value.DepthFactor, value.ClampWindow, value.MaxFrameCount, value.Item9);
}