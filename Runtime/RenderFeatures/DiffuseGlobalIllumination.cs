using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class DiffuseGlobalIllumination : CameraRenderFeature
{
    private readonly Material material;
    private readonly Settings settings;

    private readonly PersistentRTHandleCache temporalCache, temporalWeightCache;
    private readonly RayTracingShader raytracingShader;

    public DiffuseGlobalIllumination(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        material = new Material(Shader.Find("Hidden/ScreenSpaceGlobalIllumination")) { hideFlags = HideFlags.HideAndDontSave };
        this.settings = settings;

        temporalCache = new PersistentRTHandleCache(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "SSGI Color", isScreenTexture: true);
		temporalWeightCache = new PersistentRTHandleCache(GraphicsFormat.R16_UNorm, renderGraph, "SSGI Weight", isScreenTexture: true);
        raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Diffuse");
    }

    protected override void Cleanup(bool disposing)
    {
        temporalCache.Dispose();
        temporalWeightCache.Dispose();
    }

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
		using var scope = renderGraph.AddProfileScope("Diffuse Global Illumination");

        var tempResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
        var hitResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
        var previousFrame = renderGraph.GetResource<PreviousColor>().Handle;

        var depth = renderGraph.GetResource<CameraDepthData>().Handle;
        var normalRoughness = renderGraph.GetResource<NormalRoughnessData>().Handle;
        if (settings.UseRaytracing)
        {
            // Need to set some things as globals so that hit shaders can access them..
            using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Specular GI Raytrace Setup"))
            {
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TerrainRenderData>(true);
                pass.AddRenderPassData<CloudShadowDataResult>();
                pass.AddRenderPassData<ViewData>();
                pass.AddRenderPassData<FrameData>();
                pass.AddRenderPassData<EnvironmentData>();
                pass.AddRenderPassData<LightingData>();
			}
            using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Diffuse GI Raytrace"))
            {
                var raytracingData = renderGraph.GetResource<RaytracingResult>();

                pass.Initialize(raytracingShader, "RayGeneration", "Raytracing", raytracingData.Rtas, camera.scaledPixelWidth, camera.scaledPixelHeight, 1, raytracingData.Bias, raytracingData.DistantBias, camera.TanHalfFov());
                pass.WriteTexture(tempResult, "HitColor");
                pass.WriteTexture(hitResult, "HitResult");
				pass.AddRenderPassData<NormalRoughnessData>();
				pass.AddRenderPassData<PreviousColor>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<FrameData>();
                pass.AddRenderPassData<ViewData>();
                pass.AddRenderPassData<CameraDepthData>();
                pass.AddRenderPassData<EnvironmentData>();
                pass.AddRenderPassData<LightingData>();
			}
		}
        else
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Trace"))
            {
                pass.Initialize(material);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(tempResult);
                pass.WriteTexture(hitResult);
                pass.ConfigureClear(RTClearFlags.Color);

                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<ViewData>();
                pass.AddRenderPassData<FrameData>();
                pass.AddRenderPassData<BentNormalOcclusionData>();
                pass.AddRenderPassData<VelocityData>();
                pass.AddRenderPassData<HiZMinDepthData>();
                pass.AddRenderPassData<CameraDepthData>();
				pass.AddRenderPassData<NormalRoughnessData>();
				pass.AddRenderPassData<PreviousColor>();

				pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_Intensity", settings.Intensity);
                    pass.SetFloat("_MaxSteps", settings.MaxSamples);
                    pass.SetFloat("_Thickness", settings.Thickness);
                    pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(camera.scaledPixelWidth, camera.scaledPixelHeight) - 1);
                    pass.SetVector("_PreviousColorScaleLimit", pass.GetScaleLimit2D(previousFrame));
                    pass.SetFloat("_ConeAngle", Mathf.Tan(0.5f * settings.ConeAngle * Mathf.Deg2Rad) * (camera.scaledPixelHeight / camera.TanHalfFov() * 0.5f));
                });
            }
        }

        var spatialResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
        var spatialWeight = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_UNorm, isScreenTexture: true);
        var rayDepth = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_SFloat, isScreenTexture: true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Spatial"))
        {
            pass.Initialize(material, 1);
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(rayDepth, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(spatialWeight, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_Input", tempResult);
            pass.ReadTexture("_HitResult", hitResult);

            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<SkyReflectionAmbientData>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<BentNormalOcclusionData>();
            pass.AddRenderPassData<VelocityData>();
            pass.AddRenderPassData<CameraDepthData>();
            pass.AddRenderPassData<CameraStencilData>();
			pass.AddRenderPassData<NormalRoughnessData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_Intensity", settings.Intensity);
                pass.SetFloat("_MaxSteps", settings.MaxSamples);
                pass.SetFloat("_Thickness", settings.Thickness);
                pass.SetInt("_ResolveSamples", settings.ResolveSamples);
                pass.SetFloat("_ResolveSize", settings.ResolveSize);
                pass.SetFloat("DiffuseGiStrength", settings.Intensity);
            });
        }

        var (current, history, wasCreated) = temporalCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
        var (currentWeight, historyWeight, wasCreatedWeight) = temporalWeightCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Temporal"))
        {
            pass.Initialize(material, 2);
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(currentWeight, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_TemporalInput", spatialResult);
            pass.ReadTexture("_History", history);
            pass.ReadTexture("_HitResult", hitResult);
            pass.ReadTexture("RayDepth", rayDepth);
            pass.ReadTexture("WeightInput", spatialWeight);
            pass.ReadTexture("WeightHistory", historyWeight);

            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<SkyReflectionAmbientData>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<BentNormalOcclusionData>();
            pass.AddRenderPassData<VelocityData>();
            pass.AddRenderPassData<CameraDepthData>();
            pass.AddRenderPassData<CameraStencilData>();
			pass.AddRenderPassData<NormalRoughnessData>();

			pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_IsFirst", wasCreated ? 1.0f : 0.0f);
                pass.SetVector("_HistoryScaleLimit", pass.GetScaleLimit2D(history));
                pass.SetFloat("_Intensity", settings.Intensity);
                pass.SetFloat("_MaxSteps", settings.MaxSamples);
                pass.SetFloat("_Thickness", settings.Thickness);
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

		void IRenderPassData.SetInputs(RenderPassBase pass)
		{
            pass.ReadTexture("ScreenSpaceGlobalIllumination", ScreenSpaceGlobalIllumination);
		}

		void IRenderPassData.SetProperties(RenderPassBase pass, CommandBuffer command)
		{
			pass.SetVector("ScreenSpaceGlobalIlluminationScaleLimit", pass.GetScaleLimit2D(ScreenSpaceGlobalIllumination));
			pass.SetFloat("DiffuseGiStrength", intensity);
		}
	}
}
