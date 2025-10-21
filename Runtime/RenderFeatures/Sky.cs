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
		using (var pass = renderGraph.AddFullscreenRenderPass("Temporal", (history, wasCreated, settings.StationaryBlend, settings.MotionBlend, settings.MotionFactor, settings.DepthFactor, settings.ClampWindow, settings.MaxFrameCount, camera.ScaledViewSize())))
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
				pass.SetVector("_SkyHistoryScaleLimit", pass.GetScaleLimit2D(data.history));

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
