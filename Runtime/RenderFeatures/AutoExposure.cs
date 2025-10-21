using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public partial class AutoExposure : CameraRenderFeature
{
	private readonly Tonemapping.Settings tonemappingSettings;
	private readonly Settings settings;
	private readonly LensSettings lensSettings;
	private readonly ComputeShader computeShader;
	private readonly Texture2D exposureTexture;

	public AutoExposure(Settings settings, LensSettings lensSettings, RenderGraph renderGraph, Tonemapping.Settings tonemappingSettings) : base(renderGraph)
	{
		this.settings = settings;
		this.lensSettings = lensSettings;
		this.tonemappingSettings = tonemappingSettings;
		computeShader = Resources.Load<ComputeShader>("PostProcessing/AutoExposure");

		var exposurePixels = ArrayPool<float>.Get(settings.ExposureResolution);
		for (var i = 0; i < settings.ExposureResolution; i++)
		{
			var uv = i / (settings.ExposureResolution - 1f);
			var t = Mathf.Lerp(settings.MinEv, settings.MaxEv, uv);
			var exposure = settings.ExposureCurve.Evaluate(t);
			exposurePixels[i] = exposure;
		}

		exposureTexture = new Texture2D(settings.ExposureResolution, 1, TextureFormat.RFloat, false) { hideFlags = HideFlags.HideAndDontSave };
		exposureTexture.SetPixelData(exposurePixels, 0);
		ArrayPool<float>.Release(exposurePixels);
		exposureTexture.Apply(false, false);
	}

	protected override void Cleanup(bool disposing)
	{
		// TODO: Rendergraph?
		Object.DestroyImmediate(exposureTexture);
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		// TODO: Only do when changed
		var exposurePixels = exposureTexture.GetRawTextureData<float>();
		for (var i = 0; i < settings.ExposureResolution; i++)
		{
			var uv = i / (settings.ExposureResolution - 1f);
			var t = Mathf.Lerp(settings.MinEv, settings.MaxEv, uv);
			var exposure = settings.ExposureCurve.Evaluate(t);
			exposurePixels[i] = exposure;
		}

		exposureTexture.SetPixelData(exposurePixels, 0);
		exposureTexture.Apply(false, false);

		var histogram = renderGraph.GetBuffer(256);
		using (var pass = renderGraph.AddComputeRenderPass("Auto Exposure", 
		(
			minEv: settings.MinEv,
			maxEv: settings.MaxEv,
			adaptationSpeed: settings.AdaptationSpeed,
			exposureCompensation: settings.ExposureCompensation,
			iso: lensSettings.Iso,
			aperture: lensSettings.Aperture,
			shutterSpeed: lensSettings.ShutterSpeed,
			histogramMin: settings.HistogramMin,
			histogramMax: settings.HistogramMax,
			exposureCompensationRemap: GraphicsUtilities.HalfTexelRemap(settings.ExposureResolution, 1),
			meteringMode: (float)settings.MeteringMode,
			settings.ProceduralCenter,
			settings.ProceduralRadii,
			settings.ProceduralSoftness
		)))
		{
			pass.Initialize(computeShader, 0, camera.scaledPixelWidth, camera.scaledPixelHeight);
			pass.ReadTexture(nameof(CameraTarget), renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteBuffer("LuminanceHistogram", histogram);
			pass.AddRenderPassData<AutoExposureData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("MinEv", data.minEv);
				pass.SetFloat("MaxEv", data.maxEv);
				pass.SetFloat("AdaptationSpeed", data.adaptationSpeed);
				pass.SetFloat("ExposureCompensation", data.exposureCompensation);
				pass.SetFloat("Iso", data.iso);
				pass.SetFloat("Aperture", data.aperture);
				pass.SetFloat("ShutterSpeed", data.shutterSpeed);
				pass.SetFloat("HistogramMin", data.histogramMin);
				pass.SetFloat("HistogramMax", data.histogramMax);
				pass.SetFloat("MeteringMode", data.meteringMode);
				pass.SetVector("ExposureCompensationRemap", data.exposureCompensationRemap);
				pass.SetVector("ProceduralCenter", data.ProceduralCenter);
				pass.SetVector("ProceduralRadii", data.ProceduralRadii);
				pass.SetFloat("ProceduralSoftness", data.ProceduralSoftness);
			});
		}

		var output = renderGraph.GetBuffer(1, 16, target: GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
		var currentData = renderGraph.GetResource<AutoExposureData>();

		using (var pass = renderGraph.AddComputeRenderPass("Auto Exposure", (settings.ExposureMode, exposureTexture, currentData.IsFirst, tonemappingSettings.PaperWhite)))
		{
			pass.Initialize(computeShader, 1, 1);
			pass.ReadBuffer("LuminanceHistogram", histogram);
			pass.WriteBuffer("LuminanceOutput", output);
			pass.AddRenderPassData<AutoExposureData>();
			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("Mode", (float)data.ExposureMode);
				pass.SetFloat("IsFirst", data.IsFirst ? 1 : 0);
				pass.SetTexture("ExposureCompensationTexture", data.exposureTexture);
				pass.SetFloat("PaperWhite", data.PaperWhite);
			});
		}

		var exposureBuffer = renderGraph.GetResource<AutoExposureData>().ExposureBuffer;
		using (var pass = renderGraph.AddGenericRenderPass("Auto Exposure", (output, exposureBuffer, settings.DebugExposure)))
		{
			pass.ReadBuffer("", output);
			pass.WriteBuffer("", exposureBuffer);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				command.CopyBuffer(pass.GetBuffer(data.output), pass.GetBuffer(data.exposureBuffer));

				if (data.DebugExposure)
				{
					command.RequestAsyncReadback(pass.GetBuffer(data.exposureBuffer), readback =>
					{
						var data = readback.GetData<float>();
						var exposure = data[0];
						var sensitivity = 100.0f;
						var lensAttenuation = 0.65f;
						var lensImperfectionExposureScale = 78.0f / (sensitivity * lensAttenuation);
						var reflectedLightMeterConstant = 12.5f;
						var exposureCompensation = data[3];
						var ev100 = -Math.Log2(lensImperfectionExposureScale * exposure) + exposureCompensation;
						var luminance = Math.Exp2(ev100) * (reflectedLightMeterConstant / sensitivity);
						Debug.Log($"EV: {ev100}. ({luminance} cd/m^2)");
					});
				}
			});
		}
	}
}