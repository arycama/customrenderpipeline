using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class ScreenSpaceReflections : CameraRenderFeature
{
    private readonly Material material;
    private readonly Settings settings;

    private readonly PersistentRTHandleCache temporalCache, temporalWeightCache;
    private readonly RayTracingShader raytracingShader;

    public ScreenSpaceReflections(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.settings = settings;

        material = new Material(Shader.Find("Hidden/ScreenSpaceReflections")) { hideFlags = HideFlags.HideAndDontSave };
        temporalCache = new PersistentRTHandleCache(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Screen Space Reflections", isScreenTexture: true);
		temporalWeightCache = new PersistentRTHandleCache(GraphicsFormat.R16_UNorm, renderGraph, "SSGI Weight", isScreenTexture: true);
		raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Specular");
    }

    protected override void Cleanup(bool disposing)
    {
        temporalCache.Dispose();
		temporalWeightCache.Dispose();
	}

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
		using var scope = renderGraph.AddProfileScope("Specular Global Illumination");

        // Must be screen texture since we use stencil to skip sky pixels
        var tempResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R32G32B32A32_SFloat, isScreenTexture: true, clearFlags: RTClearFlags.Color);

        // Slight fuzzyness with 16 bits, probably due to depth.. would like to investigate
        var hitResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R32G32B32A32_SFloat, isScreenTexture: true, clearFlags: RTClearFlags.Color);

        var depth = renderGraph.GetRTHandle<CameraDepth>().handle;
        var normalRoughness = renderGraph.GetRTHandle<GBufferNormalRoughness>().handle;
        var previousFrame = renderGraph.GetRTHandle<PreviousCameraTarget>().handle;
        var albedoMetallic = renderGraph.GetRTHandle<GBufferAlbedoMetallic>().handle;

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
				pass.AddRenderPassData<WaterPrepassResult>();
			}

            using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Specular GI Raytrace"))
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
				pass.AddRenderPassData<WaterPrepassResult>();
			}
		}
        else
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Reflections Trace"))
            {
                pass.Initialize(material);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(tempResult, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(hitResult, RenderBufferLoadAction.DontCare);
                pass.ReadTexture("", depth);

                pass.AddRenderPassData<SkyReflectionAmbientData>();
               // pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<ViewData>();
                pass.AddRenderPassData<FrameData>();
                pass.ReadRtHandle<GBufferBentNormalOcclusion>();
                pass.ReadRtHandle<CameraVelocity>();
                pass.ReadRtHandle<HiZMinDepth>();
                pass.ReadRtHandle<CameraDepth>();
                pass.ReadRtHandle<CameraStencil>();
				pass.ReadRtHandle<GBufferNormalRoughness>();
				pass.ReadRtHandle<PreviousCameraTarget>();
				pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_MaxSteps", settings.MaxSamples);
                    pass.SetFloat("_Thickness", settings.Thickness);
                    pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(camera.scaledPixelWidth, camera.scaledPixelHeight) - 1);
                    pass.SetVector("PreviousCameraTargetScaleLimit", pass.GetScaleLimit2D(previousFrame));
                });
            }
        }

        var spatialResult = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
        var spatialWeight = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_UNorm, isScreenTexture: true);
		var rayDepth = renderGraph.GetTexture(camera.scaledPixelWidth, camera.scaledPixelHeight, GraphicsFormat.R16_SFloat, isScreenTexture: true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Specular GI Spatial"))
        {
            pass.Initialize(material, 1);
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(rayDepth, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(spatialWeight, RenderBufferLoadAction.DontCare);

			pass.ReadTexture("_Input", tempResult);
            pass.ReadTexture("_HitResult", hitResult);
			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadRtHandle<GBufferAlbedoMetallic>();

            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<SkyReflectionAmbientData>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<ViewData>();
            pass.ReadRtHandle<GBufferBentNormalOcclusion>();
            pass.ReadRtHandle<CameraVelocity>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("_ResolveSamples", settings.ResolveSamples);
                pass.SetFloat("_ResolveSize", settings.ResolveSize);
                pass.SetFloat("SpecularGiStrength", settings.Intensity);
            });
        }

        var (current, history, wasCreated) = temporalCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
        var (currentWeight, historyWeight, wasCreatedWeight) = temporalWeightCache.GetTextures(camera.scaledPixelWidth, camera.scaledPixelHeight, camera);
		using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Reflections Temporal"))
        {
            pass.Initialize(material, 2);
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
			pass.WriteTexture(currentWeight, RenderBufferLoadAction.DontCare);

			pass.ReadTexture("_TemporalInput", spatialResult);
            pass.ReadTexture("_History", history);
			pass.ReadRtHandle<GBufferNormalRoughness>();
			pass.ReadRtHandle<GBufferAlbedoMetallic>();
			pass.ReadTexture("RayDepth", rayDepth);
			pass.ReadTexture("WeightInput", spatialWeight);
			pass.ReadTexture("WeightHistory", historyWeight);

			pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<SkyReflectionAmbientData>();
            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<ViewData>();
            pass.ReadRtHandle<GBufferBentNormalOcclusion>();
            pass.ReadRtHandle<CameraVelocity>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<CameraStencil>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_IsFirst", wasCreated ? 1.0f : 0.0f);
                pass.SetVector("_HistoryScaleLimit", pass.GetScaleLimit2D(history));
            });
        }

        renderGraph.SetResource(new ScreenSpaceReflectionResult(current, settings.Intensity)); ;
    }
}
