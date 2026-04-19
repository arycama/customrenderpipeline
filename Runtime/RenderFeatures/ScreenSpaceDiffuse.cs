using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class ScreenSpaceDiffuse : ViewRenderFeature
{
    private readonly Material material;
    private readonly Settings settings;

    private readonly PersistentRTHandleCache temporalCache, temporalWeightCache;
    private readonly RayTracingShader raytracingShader;

    public ScreenSpaceDiffuse(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        material = new Material(Shader.Find("Hidden/ScreenSpaceGlobalIllumination")) { hideFlags = HideFlags.HideAndDontSave };
        this.settings = settings;

        temporalCache = new PersistentRTHandleCache(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "SSGI Color", isScreenTexture: true);
		temporalWeightCache = new PersistentRTHandleCache(GraphicsFormat.R8_UNorm, renderGraph, "SSGI Weight", isScreenTexture: true);
        raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Diffuse");
    }

    protected override void Cleanup(bool disposing)
    {
        temporalCache.Dispose();
        temporalWeightCache.Dispose();
    }

    public override void Render(ViewRenderData viewRenderData)
    {
        if (settings.Intensity == 0)
            return;

		using var scope = renderGraph.AddProfileScope("Diffuse Global Illumination");

        var tempResult = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true, clear: true);
        var hitResult = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true, clear: true);

        if (settings.UseRaytracing)
        {
            // Need to set some things as globals so that hit shaders can access them..
            using (var pass = renderGraph.AddGenericRenderPass("Specular GI Raytrace Setup"))
            {
                pass.ReadResource<SkyReflectionAmbientData>();
                pass.ReadResource<LightingSetup.Result>();
                pass.ReadResource<AutoExposureData>();
                pass.ReadResource<AtmospherePropertiesAndTables>();
			    pass.ReadResource<TerrainFrameData>(true);
                pass.ReadResource<TerrainViewData>(true);
                pass.ReadResource<CloudShadowDataResult>();
                pass.ReadResource<ViewData>();
                pass.ReadResource<FrameData>();
                pass.ReadResource<EnvironmentData>();
                pass.ReadResource<LightingData>();
			}
            using (var pass = renderGraph.AddRaytracingRenderPass("Diffuse GI Raytrace"))
            {
                var raytracingData = renderGraph.GetResource<RaytracingResult>();

                pass.Initialize(raytracingShader, "RayGeneration", "Raytracing", raytracingData.Rtas, viewRenderData.viewSize.x, viewRenderData.viewSize.y, 1, raytracingData.Bias, raytracingData.DistantBias, viewRenderData.tanHalfFov.y);
                pass.WriteTexture(tempResult, "HitColor");
                pass.WriteTexture(hitResult, "HitResult");
				pass.ReadRtHandle<GBufferNormalRoughness>();
				pass.ReadRtHandle<PreviousCameraTarget>();
                pass.ReadResource<SkyReflectionAmbientData>();
                pass.ReadResource<LightingSetup.Result>();
                pass.ReadResource<AutoExposureData>();
                pass.ReadResource<FrameData>();
                pass.ReadResource<ViewData>();
                pass.ReadRtHandle<CameraDepth>();
                pass.ReadResource<EnvironmentData>();
                pass.ReadResource<LightingData>();
			}
		}
        else
        {
			using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Global Illumination Trace", (settings.Intensity, settings.MaxSamples, settings.Thickness, viewRenderData.viewSize, settings.ConeAngle, viewRenderData.tanHalfFov.y)))
			{
				pass.Initialize(material, viewRenderData.viewSize, viewRenderData.viewCount);
                pass.PreventNewSubPass = true;

                pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
				pass.WriteTexture(tempResult);
				pass.WriteTexture(hitResult);

				pass.ReadResource<LightingSetup.Result>();
				pass.ReadResource<TemporalAAData>();
				pass.ReadResource<AutoExposureData>();
				pass.ReadResource<SkyReflectionAmbientData>();
				pass.ReadResource<AtmospherePropertiesAndTables>();
				pass.ReadResource<ViewData>();
				pass.ReadResource<FrameData>();
				pass.ReadRtHandle<GBufferBentNormalOcclusion>();
				pass.ReadRtHandle<CameraVelocity>();
				pass.ReadRtHandle<HiZMinDepth>();
				pass.ReadRtHandle<CameraDepth>();
				pass.ReadRtHandle<GBufferNormalRoughness>();
				pass.ReadRtHandle<PreviousCameraTarget>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetFloat("Intensity", data.Intensity);
					pass.SetFloat("MaxSteps", data.MaxSamples);
					pass.SetFloat("Thickness", data.Thickness);
					pass.SetFloat("MaxMip", Texture2DExtensions.MipCount(data.viewSize) - 1);
					pass.SetFloat("ConeAngle", Mathf.Tan(0.5f * data.ConeAngle * Mathf.Deg2Rad) * (data.viewSize.y / data.y * 0.5f));
				});
			}
        }

        var spatialResult = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
        var spatialWeight = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R8_UNorm, isScreenTexture: true);
        var rayDepth = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.R16_SFloat, isScreenTexture: true);
        using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Global Illumination Spatial", (settings.Intensity, settings.MaxSamples, settings.Thickness, settings.ResolveSamples, settings.ResolveSize)))
        {
            pass.Initialize(material, viewRenderData.viewSize, viewRenderData.viewCount, 1);
            pass.PreventNewSubPass = true;

            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(spatialResult);
            pass.WriteTexture(rayDepth);
            pass.WriteTexture(spatialWeight);

            pass.ReadTexture("Input", tempResult);
            pass.ReadTexture("HitResult", hitResult);

            pass.ReadResource<TemporalAAData>();
            pass.ReadResource<SkyReflectionAmbientData>();
            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<AutoExposureData>();
            pass.ReadResource<ViewData>();
            pass.ReadResource<FrameData>();
            pass.ReadRtHandle<GBufferAlbedoMetallic>();
            pass.ReadRtHandle<GBufferBentNormalOcclusion>();
            pass.ReadRtHandle<CameraVelocity>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();
			pass.ReadRtHandle<GBufferNormalRoughness>();
            pass.ReadRtHandle<PreviousCameraDepth>();
            pass.ReadRtHandle<PreviousCameraVelocity>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("Intensity", data.Intensity);
                pass.SetFloat("MaxSteps", data.MaxSamples);
                pass.SetFloat("Thickness", data.Thickness);
                pass.SetInt("ResolveSamples", data.ResolveSamples);
                pass.SetFloat("ResolveSize", data.ResolveSize);
                pass.SetFloat("DiffuseGiStrength", data.Intensity);
            });
        }

		bool wasCreated = default;
		ResourceHandle<RenderTexture> current, history = default;

        using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Global Illumination Temporal", (wasCreated, history, settings.Intensity, settings.MaxSamples, settings.Thickness)))
        {
			(current, history, wasCreated) = temporalCache.GetTextures(viewRenderData.viewSize, pass.Index, viewRenderData.viewId);
			var (currentWeight, historyWeight, wasCreatedWeight) = temporalWeightCache.GetTextures(viewRenderData.viewSize, pass.Index, viewRenderData.viewId);

			pass.renderData.wasCreated = wasCreated;
			pass.renderData.history = history;

			pass.Initialize(material, viewRenderData.viewSize, viewRenderData.viewCount, 2);
            pass.PreventNewSubPass = true;

            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(current);
            pass.WriteTexture(currentWeight);

            pass.ReadTexture("TemporalInput", spatialResult);
            pass.ReadTexture("History", history);
            pass.ReadTexture("HitResult", hitResult);
            pass.ReadTexture("RayDepth", rayDepth);
            pass.ReadTexture("WeightInput", spatialWeight);
            pass.ReadTexture("WeightHistory", historyWeight);

            pass.ReadResource<TemporalAAData>();
            pass.ReadResource<AutoExposureData>();
            pass.ReadResource<SkyReflectionAmbientData>();
            pass.ReadResource<AtmospherePropertiesAndTables>();
            pass.ReadResource<ViewData>();
            pass.ReadResource<FrameData>();
            pass.ReadRtHandle<GBufferBentNormalOcclusion>();
            pass.ReadRtHandle<CameraVelocity>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();
			pass.ReadRtHandle<GBufferNormalRoughness>();

			pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("IsFirst", data.wasCreated ? 1.0f : 0.0f);
                pass.SetVector("HistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.history));
                pass.SetFloat("Intensity", data.Intensity);
                pass.SetFloat("MaxSteps", data.MaxSamples);
                pass.SetFloat("Thickness", data.Thickness);
            });
        }

        renderGraph.SetResource(new Result(current, settings.Intensity)); ;
    }

    public readonly struct Result : IRenderPassData
    {
        public ResourceHandle<RenderTexture> ScreenSpaceGlobalIllumination { get; }
        private readonly float intensity;

        public Result(ResourceHandle<RenderTexture> screenSpaceGlobalIllumination, float intensity)
        {
            ScreenSpaceGlobalIllumination = screenSpaceGlobalIllumination;
            this.intensity = intensity;
        }

		void IRenderPassData.SetInputs(RenderPass pass)
		{
            pass.ReadTexture("ScreenSpaceGlobalIllumination", ScreenSpaceGlobalIllumination);
		}

		void IRenderPassData.SetProperties(RenderPass pass, CommandBuffer command)
		{
			pass.SetVector("ScreenSpaceGlobalIlluminationScaleLimit", pass.RenderGraph.GetScaleLimit2D(ScreenSpaceGlobalIllumination));
			pass.SetFloat("DiffuseGiStrength", intensity);
		}
	}
}
