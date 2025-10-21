using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class AmbientOcclusion : CameraRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField] public bool AmbientOcclusion { get; private set; } = true;
		[field: SerializeField] public bool Raytracing { get; private set; } = true;
		[field: SerializeField, Min(0)] public float AoStrength { get; private set; } = 1;
		[field: SerializeField, Min(0)] public float Radius { get; private set; } = 5;
		[field: SerializeField, Range(1, 8)] public int Directions { get; private set; } = 4;
		[field: SerializeField, Range(1, 32)] public int Samples { get; private set; } = 8;
		[field: SerializeField, Range(0, 1)] public float Falloff { get; private set; } = 0.75f;
		[field: SerializeField, Range(0, 1)] public float MaxScreenRadius { get; private set; } = 0.15f;
		[field: SerializeField, Range(0, 0.01f)] public float ThinOccluderCompensation { get; private set; } = 0.05f;
	}

	private readonly Material material;
	private readonly Settings settings;
	private readonly RayTracingShader ambientOcclusionRaytracingShader;

	private readonly PersistentRTHandleCache temporalCache;

	public AmbientOcclusion(RenderGraph renderGraph, Settings settings) : base(renderGraph)
	{
		material = new Material(Shader.Find("Hidden/Ambient Occlusion")) { hideFlags = HideFlags.HideAndDontSave };
		this.settings = settings;
		temporalCache = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Ambient Occlusion", isScreenTexture: true);
		ambientOcclusionRaytracingShader = Resources.Load<RayTracingShader>("Raytracing/AmbientOcclusion");
	}

	protected override void Cleanup(bool disposing)
	{
		temporalCache.Dispose();
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		if (!settings.AmbientOcclusion)
			return;

		renderGraph.AddProfileBeginPass("Ambient Occlusion");

		ResourceHandle<RenderTexture> result;
		if (settings.Raytracing)
		{
			result = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
			using (var pass = renderGraph.AddRaytracingRenderPass("Raytraced Ambient Occlusion", (settings.Radius, settings.Falloff)))
			{
				var raytracingData = renderGraph.GetResource<RaytracingResult>();

				pass.Initialize(ambientOcclusionRaytracingShader, "RayGeneration", "RaytracingVisibility", raytracingData.Rtas, camera.scaledPixelWidth, camera.scaledPixelHeight, 1, raytracingData.Bias, raytracingData.DistantBias, camera.fieldOfView);
				pass.WriteTexture(result, "HitResult");

				pass.ReadRtHandle<CameraDepth>();
				pass.ReadRtHandle<GBufferNormalRoughness>();
				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetFloat("Radius", data.Radius);
					pass.SetFloat("Falloff", data.Falloff);
				});
			}
		}
		else
		{
			result = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
			using (var pass = renderGraph.AddFullscreenRenderPass("Ambient Occlusion Compute", (settings.Radius, settings.Directions, settings.Samples, settings.Falloff, settings.MaxScreenRadius, settings.ThinOccluderCompensation)))
			{
				pass.Initialize(material, 0);
				pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
				pass.WriteTexture(result);

				pass.ReadRtHandle<CameraDepth>();
				pass.ReadRtHandle<GBufferNormalRoughness>();
				pass.AddRenderPassData<FrameData>();
				pass.AddRenderPassData<ViewData>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetFloat("Radius", data.Radius);
					pass.SetFloat("Directions", data.Directions);
					pass.SetFloat("Samples", data.Samples);
					pass.SetFloat("Falloff", data.Falloff);
					pass.SetFloat("MaxScreenRadius", data.MaxScreenRadius);
					pass.SetFloat("ThinOccluderCompensation", data.ThinOccluderCompensation);
				});
			}
		}

		var (current, history, wasCreated) = temporalCache.GetTextures(camera.pixelWidth, camera.pixelHeight, camera);
		using (var pass = renderGraph.AddFullscreenRenderPass("Ambient Occlusion Temporal", (wasCreated, history)))
		{
			pass.Initialize(material, 1);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
			pass.WriteTexture(current);
			pass.ReadTexture("Input", result);
			pass.ReadTexture("History", history);

			pass.AddRenderPassData<FrameData>();
			pass.ReadRtHandle<CameraDepth>();
			pass.ReadRtHandle<CameraVelocity>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.ReadRtHandle<PreviousCameraVelocity>();
			pass.ReadRtHandle<PreviousCameraDepth>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("HasHistory", data.wasCreated ? 0 : 1);
				pass.SetVector("HistoryScaleLimit", pass.GetScaleLimit2D(data.history));
			});
		}

		var output = renderGraph.GetTexture(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true);
		using (var pass = renderGraph.AddFullscreenRenderPass("Ambient Occlusion Combine", (settings.AoStrength, current)))
		{
			pass.Initialize(material, 2);
			pass.WriteTexture(output);
			pass.ReadTexture("Input", current);
			pass.ReadRtHandle<GBufferBentNormalOcclusion>();
			pass.AddRenderPassData<TemporalAAData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("Strength", data.AoStrength);
				pass.SetVector("InputScaleLimit", pass.GetScaleLimit2D(data.current));
			});
		}

		renderGraph.SetRTHandle<GBufferBentNormalOcclusion>(output);

		renderGraph.AddProfileEndPass("Ambient Occlusion");
	}
}
