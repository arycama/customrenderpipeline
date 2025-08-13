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
		renderGraph.AddProfileBeginPass("Sky Render");

		var starsTemp = renderGraph.GetTexture(settings.StarResolution, settings.StarResolution, GraphicsFormat.B10G11R11_UFloatPack32, dimension: TextureDimension.Cube, isExactSize: true);
		using (var pass = renderGraph.AddRenderPass<DrawProceduralRenderPass>("Render Stars"))
		{
			pass.Initialize(settings.StarMaterial, Matrix4x4.identity, 0, 4 * settings.StarCount, 1, MeshTopology.Quads);

			pass.WriteTexture(starsTemp);
			
			pass.SetRenderFunction((command, pass) =>
			{
				command.SetRenderTarget(pass.GetRenderTexture(starsTemp), 0, CubemapFace.Unknown, -1);
				command.ClearRenderTarget(true, true, Color.clear);
			});
		}

		var skyTemp = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
		var depth = renderGraph.GetResource<CameraDepthData>();
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Render Sky"))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Render Sky"));
			pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
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

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetFloat("_Samples", settings.RenderSamples);
			});
		}

		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Render Scene"))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Render Scene"));
			pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
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
			pass.AddRenderPassData<CameraDepthData>();

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetFloat("_Samples", settings.RenderSamples);
			});
		}

		// Reprojection
		var (current, history, wasCreated) = textureCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Temporal"))
		{
			pass.Initialize(skyMaterial, skyMaterial.FindPass("Temporal"));
			pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
			pass.ReadTexture("_SkyInput", skyTemp);
			pass.ReadTexture("_SkyHistory", history);

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<CloudRenderResult>();
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<PreviousDepth>();
			pass.AddRenderPassData<PreviousVelocity>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<VelocityData>();
			pass.AddRenderPassData<CameraDepthData>();

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetVector("_SkyHistoryScaleLimit", pass.GetScaleLimit2D(history));

				pass.SetFloat("_IsFirst", wasCreated ? 1.0f : 0.0f);
				pass.SetFloat("_StationaryBlend", settings.StationaryBlend);
				pass.SetFloat("_MotionBlend", settings.MotionBlend);
				pass.SetFloat("_MotionFactor", settings.MotionFactor);
				pass.SetFloat("_DepthFactor", settings.DepthFactor);
				pass.SetFloat("_ClampWindow", settings.ClampWindow);

				pass.SetFloat("_MaxFrameCount", settings.MaxFrameCount);

				pass.SetInt("_MaxWidth", camera.scaledPixelWidth - 1);
				pass.SetInt("_MaxHeight", camera.scaledPixelHeight - 1);
			});
		}

		renderGraph.SetResource(new SkyResultData(current, starsTemp));
		renderGraph.AddProfileEndPass("Sky Render");
	}
}
