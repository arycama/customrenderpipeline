using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class ScreenSpaceSpecular : ViewRenderFeature
{
    private readonly Material material;
    private readonly Settings settings;

    private readonly PersistentRTHandleCache temporalCache, speedCache, opacityCache;
    private readonly RayTracingShader raytracingShader;

    public ScreenSpaceSpecular(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        this.settings = settings;

        material = new Material(Shader.Find("Hidden/ScreenSpaceReflections")) { hideFlags = HideFlags.HideAndDontSave };
        temporalCache = new PersistentRTHandleCache(GraphicsFormat.R32_UInt, renderGraph, "Screen Space Reflections", isScreenTexture: true);
        speedCache = new PersistentRTHandleCache(GraphicsFormat.R8_UNorm, renderGraph, "SSGI Weight", isScreenTexture: true);
        opacityCache = new PersistentRTHandleCache(GraphicsFormat.R8_UNorm, renderGraph, "SSGI Weight", isScreenTexture: true);
        raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Specular");
    }

    protected override void Cleanup(bool disposing)
    {
        temporalCache.Dispose();
        speedCache.Dispose();
        opacityCache.Dispose();
    }

    public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
        if (settings.Intensity == 0)
            return;

        using var scope = renderGraph.AddProfileScope("Specular Global Illumination");

        var tempResult = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true, clear: true);
        var hitResult = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true, clear: true);
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
                pass.ReadResource<WaterPrepassResult>();
            }

            using (var pass = renderGraph.AddRaytracingRenderPass("Specular GI Raytrace"))
            {
                var raytracingData = renderGraph.GetResource<RaytracingResult>();

                pass.Initialize(raytracingShader, "RayGeneration", "Raytracing", raytracingData.Rtas, viewPassData.viewSize.x, viewPassData.viewSize.y, 1, raytracingData.Bias, raytracingData.DistantBias, viewPassData.tanHalfFov.y);
                pass.WriteTexture(tempResult, "HitColor");
                pass.WriteTexture(hitResult, "HitResult");
                pass.ReadRtHandle<GBufferNormalRoughness>();
                pass.ReadRtHandle<SceneColor>();
                pass.ReadResource<SkyReflectionAmbientData>();
                pass.ReadResource<LightingSetup.Result>();
                pass.ReadResource<AutoExposureData>();
                pass.ReadResource<FrameData>();
                pass.ReadResource<ViewData>();
                pass.ReadRtHandle<CameraDepth>();
                pass.ReadResource<EnvironmentData>();
                pass.ReadResource<LightingData>();
                pass.ReadResource<WaterPrepassResult>();
            }
        }
        else
        {
            var thicknessScale = 1.0f / (1.0f + settings.Thickness);
            var thicknessOffset = -viewPassData.near / (viewPassData.far - viewPassData.near) * (settings.Thickness * thicknessScale);
            var maxMip = Texture2DExtensions.MipCount(viewPassData.viewSize) - 1;
            var coneAngle = viewPassData.viewSize.y * 0.5f / viewPassData.tanHalfFov.y;

            using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Reflections Trace", (settings.MaxSamples, thicknessScale, thicknessOffset, maxMip, settings.Thickness, coneAngle, settings.RoughnessBias)))
            {
                pass.Initialize(material, viewPassData.viewSize, viewPassData.viewCount, isScreenPass: true);
                pass.PreventNewSubPass = true;
                pass.WriteRtHandleDepth<CameraDepth>(SubPassFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(tempResult);
                pass.WriteTexture(hitResult);
                pass.ReadRtHandle<CameraDepth>();

                pass.ReadResource<AutoExposureData>();
                pass.ReadResource<ViewData>();
                pass.ReadResource<FrameData>();
                pass.ReadRtHandle<CameraVelocity>();
                pass.ReadRtHandle<HiZMinDepth>();
                pass.ReadRtHandle<CameraDepth>();
                pass.ReadRtHandle<GBufferNormalRoughness>();
                pass.ReadRtHandle<SceneColor>();
                pass.ReadResource<TemporalAAData>();

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetInt("MaxSteps", data.MaxSamples);
                    pass.SetFloat("Thickness", data.Thickness);
                    pass.SetFloat("ThicknessScale", data.thicknessScale);
                    pass.SetFloat("ThicknessOffset", data.thicknessOffset);
                    pass.SetInt("MaxMip", data.maxMip);
                    pass.SetFloat("ConeAngle", data.coneAngle);
                    pass.SetFloat("RoughnessBias", data.RoughnessBias);
                });
            }
        }

        var spatialResult = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
        var spatialWeight = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.R8_UNorm, isScreenTexture: true);
        var rayDepth = renderGraph.GetTexture(viewPassData.viewSize, GraphicsFormat.R16_SFloat, isScreenTexture: true);
        using (var pass = renderGraph.AddFullscreenRenderPass("Specular GI Spatial", (settings.ResolveSamples, settings.ResolveSize, settings.Intensity)))
        {
            pass.Initialize(material, viewPassData.viewSize, viewPassData.viewCount, 1, isScreenPass: true);
            pass.PreventNewSubPass = true;
            pass.WriteRtHandleDepth<CameraDepth>(SubPassFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(spatialResult);
            pass.WriteTexture(rayDepth);
            pass.WriteTexture(spatialWeight);

            pass.ReadTexture("Input", tempResult);
            pass.ReadTexture("HitResult", hitResult);

            pass.ReadRtHandle<GBufferNormalRoughness>();
            pass.ReadRtHandle<GBufferAlbedoMetallic>();
            pass.ReadResource<FrameData>();
            pass.ReadResource<ViewData>();
            pass.ReadRtHandle<CameraDepth>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetInt("ResolveSamples", data.ResolveSamples);
                pass.SetFloat("ResolveSize", data.ResolveSize);
                pass.SetFloat("SpecularGiStrength", data.Intensity);
            });
        }

        bool wasCreated = default;
        ResourceHandle<RenderTexture> current, opacity, history = default;

        using (var pass = renderGraph.AddFullscreenRenderPass("Screen Space Reflections Temporal", (wasCreated, history)))
        {
            (current, history, wasCreated) = temporalCache.GetTextures(viewPassData.viewSize, pass.Index, viewPassData.viewId);
            var (currentSpeed, speedHistory, _) = speedCache.GetTextures(viewPassData.viewSize, pass.Index, viewPassData.viewId);
            var (currentOpacity, opacityHistory, _) = opacityCache.GetTextures(viewPassData.viewSize, pass.Index, viewPassData.viewId);
            opacity = currentOpacity;

            pass.renderData.history = history;
            pass.renderData.wasCreated = wasCreated;

            pass.Initialize(material, viewPassData.viewSize, viewPassData.viewCount, 2, isScreenPass: true);
            pass.PreventNewSubPass = true;
            pass.WriteRtHandleDepth<CameraDepth>(SubPassFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(current);
            pass.WriteTexture(currentSpeed);
            pass.WriteTexture(currentOpacity);

            pass.ReadTexture("TemporalInput", spatialResult);
            pass.ReadTexture("History", history);
            pass.ReadTexture("RayDepth", rayDepth);
            pass.ReadTexture("Opacity", spatialWeight);
            pass.ReadTexture("SpeedHistory", speedHistory);
            pass.ReadTexture("OpacityHistory", opacityHistory);

            pass.ReadResource<TemporalAAData>();
            pass.ReadResource<AutoExposureData>();
            pass.ReadResource<FrameData>();
            pass.ReadResource<ViewData>();
            pass.ReadRtHandle<CameraVelocity>();
            pass.ReadRtHandle<CameraDepth>();
            pass.ReadRtHandle<PreviousCameraDepth>();
            pass.ReadRtHandle<PreviousCameraVelocity>();

            pass.SetRenderFunction(static (command, pass, data) =>
            {
                pass.SetFloat("IsFirst", data.wasCreated ? 1.0f : 0.0f);
                pass.SetVector("HistoryScaleLimit", pass.RenderGraph.GetScaleLimit2D(data.history));
            });
        }

        renderGraph.SetResource(new ScreenSpaceReflectionResult(current, opacity, settings.Intensity));
    }
}
