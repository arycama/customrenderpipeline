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

        temporalCache = new PersistentRTHandleCache(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "SSGI Color", isScreenTexture: true);
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

        var tempResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true, clearFlags: RTClearFlags.Color);
        var hitResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true, clearFlags: RTClearFlags.Color);

        if (settings.UseRaytracing)
        {
            // Need to set some things as globals so that hit shaders can access them..
            using (var pass = renderGraph.AddGenericRenderPass("Specular GI Raytrace Setup"))
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
            using (var pass = renderGraph.AddRaytracingRenderPass("Diffuse GI Raytrace"))
            {
                var raytracingData = renderGraph.GetResource<RaytracingResult>();

                pass.Initialize(raytracingShader, "RayGeneration", "Raytracing", raytracingData.Rtas, camera.scaledPixelWidth, camera.scaledPixelHeight, 1, raytracingData.Bias, raytracingData.DistantBias, camera.TanHalfFov());
                pass.WriteTexture(tempResult, "HitColor");
                pass.WriteTexture(hitResult, "HitResult");
				pass.ReadRtHandle<GBufferNormalRoughness>();
				pass.ReadRtHandle<PreviousCameraTarget>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<FrameData>();
                pass.AddRenderPassData<ViewData>();
                pass.ReadRtHandle<CameraDepth>();
                pass.AddRenderPassData<EnvironmentData>();
                pass.AddRenderPassData<LightingData>();
			}
		}
        else
        {
			using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Global Illumination Trace", (settings.Intensity, settings.MaxSamples, settings.Thickness, camera.ScaledViewSize(), settings.ConeAngle, camera.TanHalfFov())))
			{
				pass.Initialize(material);
				pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
				pass.WriteTexture(tempResult);
				pass.WriteTexture(hitResult);

				pass.AddRenderPassData<LightingSetup.Result>();
				pass.AddRenderPassData<TemporalAAData>();
				pass.AddRenderPassData<AutoExposureData>();
				pass.AddRenderPassData<SkyReflectionAmbientData>();
				pass.AddRenderPassData<AtmospherePropertiesAndTables>();
				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();
				pass.ReadRtHandle<GBufferBentNormalOcclusion>();
				pass.ReadRtHandle<CameraVelocity>();
				pass.ReadRtHandle<HiZMinDepth>();
				pass.ReadRtHandle<CameraDepth>();
				pass.ReadRtHandle<GBufferNormalRoughness>();
				pass.ReadRtHandle<PreviousCameraTarget>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					pass.SetFloat("_Intensity", data.Intensity);
					pass.SetFloat("_MaxSteps", data.MaxSamples);
					pass.SetFloat("_Thickness", data.Thickness);
					pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(data.Item4) - 1);
					pass.SetFloat("_ConeAngle", Mathf.Tan(0.5f * data.ConeAngle * Mathf.Deg2Rad) * (data.Item4.y / data.Item6 * 0.5f));
				});
			}
        }

        var spatialResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
        var spatialWeight = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_UNorm, isScreenTexture: true);
        var rayDepth = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_SFloat, isScreenTexture: true);
        using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Global Illumination Spatial", (settings.Intensity, settings.MaxSamples, settings.Thickness, settings.ResolveSamples, settings.ResolveSize)))
        {
            pass.Initialize(material, 1);
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
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
            pass.ReadRtHandle<GBufferBentNormalOcclusion>();
            pass.ReadRtHandle<CameraVelocity>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();
			pass.ReadRtHandle<GBufferNormalRoughness>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("_Intensity", data.Intensity);
                pass.SetFloat("_MaxSteps", data.MaxSamples);
                pass.SetFloat("_Thickness", data.Thickness);
                pass.SetInt("_ResolveSamples", data.ResolveSamples);
                pass.SetFloat("_ResolveSize", data.ResolveSize);
                pass.SetFloat("DiffuseGiStrength", data.Intensity);
            });
        }

        var (current, history, wasCreated) = temporalCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
        var (currentWeight, historyWeight, wasCreatedWeight) = temporalWeightCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
        using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Global Illumination Temporal", (wasCreated, history, settings.Intensity, settings.MaxSamples, settings.Thickness)))
        {
            pass.Initialize(material, 2);
            pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.ReadOnlyDepthStencil);
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
            pass.ReadRtHandle<GBufferBentNormalOcclusion>();
            pass.ReadRtHandle<CameraVelocity>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();
			pass.ReadRtHandle<GBufferNormalRoughness>();

			pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("_IsFirst", data.wasCreated ? 1.0f : 0.0f);
                pass.SetVector("_HistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.history));
                pass.SetFloat("_Intensity", data.Intensity);
                pass.SetFloat("_MaxSteps", data.MaxSamples);
                pass.SetFloat("_Thickness", data.Thickness);
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
