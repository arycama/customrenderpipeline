using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public partial class AutoExposure : CameraRenderFeature
{
	private static readonly int ExposureCompensationTextureId = Shader.PropertyToID("ExposureCompensationTexture");

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
		using (var pass = renderGraph.AddComputeRenderPass("Auto Exposure", new AutoExposureStructData
		(			
			settings.MinEv,
			settings.MaxEv,
			settings.AdaptationSpeed,
			settings.ExposureCompensation,
			lensSettings.Iso,
			lensSettings.Aperture,
			lensSettings.ShutterSpeed,
			settings.HistogramMin,
			settings.HistogramMax,
			GraphicsUtilities.HalfTexelRemap(settings.ExposureResolution, 1),
			(float)settings.MeteringMode,
			settings.ProceduralCenter,
			settings.ProceduralRadii,
			settings.ProceduralSoftness
		)))
		{
			pass.Initialize(computeShader, 0, camera.scaledPixelWidth, camera.scaledPixelHeight);
			pass.ReadTexture(nameof(CameraTarget), renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteBuffer("LuminanceHistogram", histogram);
			pass.ReadResource<AutoExposureData>();

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
			pass.ReadResource<AutoExposureData>();
			pass.ReadResource<FrameData>();
			pass.ReadResource<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("Mode", (float)data.ExposureMode);
				pass.SetFloat("IsFirst", data.IsFirst ? 1 : 0);
				pass.SetTexture(ExposureCompensationTextureId, data.exposureTexture);
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

internal struct AutoExposureStructData
{
	public float minEv;
	public float maxEv;
	public float adaptationSpeed;
	public float exposureCompensation;
	public float iso;
	public float aperture;
	public float shutterSpeed;
	public float histogramMin;
	public float histogramMax;
	public Float4 exposureCompensationRemap;
	public float meteringMode;
	public Float2 ProceduralCenter;
	public Float2 ProceduralRadii;
	public float ProceduralSoftness;

	public AutoExposureStructData(float minEv, float maxEv, float adaptationSpeed, float exposureCompensation, float iso, float aperture, float shutterSpeed, float histogramMin, float histogramMax, Float4 exposureCompensationRemap, float meteringMode, Float2 proceduralCenter, Float2 proceduralRadii, float proceduralSoftness)
	{
		this.minEv = minEv;
		this.maxEv = maxEv;
		this.adaptationSpeed = adaptationSpeed;
		this.exposureCompensation = exposureCompensation;
		this.iso = iso;
		this.aperture = aperture;
		this.shutterSpeed = shutterSpeed;
		this.histogramMin = histogramMin;
		this.histogramMax = histogramMax;
		this.exposureCompensationRemap = exposureCompensationRemap;
		this.meteringMode = meteringMode;
		ProceduralCenter = proceduralCenter;
		ProceduralRadii = proceduralRadii;
		ProceduralSoftness = proceduralSoftness;
	}

	public override bool Equals(object obj) => obj is AutoExposureStructData other && minEv == other.minEv && maxEv == other.maxEv && adaptationSpeed == other.adaptationSpeed && exposureCompensation == other.exposureCompensation && iso == other.iso && aperture == other.aperture && shutterSpeed == other.shutterSpeed && histogramMin == other.histogramMin && histogramMax == other.histogramMax && EqualityComparer<Float4>.Default.Equals(exposureCompensationRemap, other.exposureCompensationRemap) && meteringMode == other.meteringMode && EqualityComparer<Float2>.Default.Equals(ProceduralCenter, other.ProceduralCenter) && EqualityComparer<Float2>.Default.Equals(ProceduralRadii, other.ProceduralRadii) && ProceduralSoftness == other.ProceduralSoftness;

	public override int GetHashCode()
	{
		var hash = new System.HashCode();
		hash.Add(minEv);
		hash.Add(maxEv);
		hash.Add(adaptationSpeed);
		hash.Add(exposureCompensation);
		hash.Add(iso);
		hash.Add(aperture);
		hash.Add(shutterSpeed);
		hash.Add(histogramMin);
		hash.Add(histogramMax);
		hash.Add(exposureCompensationRemap);
		hash.Add(meteringMode);
		hash.Add(ProceduralCenter);
		hash.Add(ProceduralRadii);
		hash.Add(ProceduralSoftness);
		return hash.ToHashCode();
	}

	public void Deconstruct(out float minEv, out float maxEv, out float adaptationSpeed, out float exposureCompensation, out float iso, out float aperture, out float shutterSpeed, out float histogramMin, out float histogramMax, out Float4 exposureCompensationRemap, out float meteringMode, out Float2 proceduralCenter, out Float2 proceduralRadii, out float proceduralSoftness)
	{
		minEv = this.minEv;
		maxEv = this.maxEv;
		adaptationSpeed = this.adaptationSpeed;
		exposureCompensation = this.exposureCompensation;
		iso = this.iso;
		aperture = this.aperture;
		shutterSpeed = this.shutterSpeed;
		histogramMin = this.histogramMin;
		histogramMax = this.histogramMax;
		exposureCompensationRemap = this.exposureCompensationRemap;
		meteringMode = this.meteringMode;
		proceduralCenter = ProceduralCenter;
		proceduralRadii = ProceduralRadii;
		proceduralSoftness = ProceduralSoftness;
	}

	public static implicit operator (float minEv, float maxEv, float adaptationSpeed, float exposureCompensation, float iso, float aperture, float shutterSpeed, float histogramMin, float histogramMax, Float4 exposureCompensationRemap, float meteringMode, Float2 ProceduralCenter, Float2 ProceduralRadii, float ProceduralSoftness)(AutoExposureStructData value) => (value.minEv, value.maxEv, value.adaptationSpeed, value.exposureCompensation, value.iso, value.aperture, value.shutterSpeed, value.histogramMin, value.histogramMax, value.exposureCompensationRemap, value.meteringMode, value.ProceduralCenter, value.ProceduralRadii, value.ProceduralSoftness);
	public static implicit operator AutoExposureStructData((float minEv, float maxEv, float adaptationSpeed, float exposureCompensation, float iso, float aperture, float shutterSpeed, float histogramMin, float histogramMax, Float4 exposureCompensationRemap, float meteringMode, Float2 ProceduralCenter, Float2 ProceduralRadii, float ProceduralSoftness) value) => new AutoExposureStructData(value.minEv, value.maxEv, value.adaptationSpeed, value.exposureCompensation, value.iso, value.aperture, value.shutterSpeed, value.histogramMin, value.histogramMax, value.exposureCompensationRemap, value.meteringMode, value.ProceduralCenter, value.ProceduralRadii, value.ProceduralSoftness);
}