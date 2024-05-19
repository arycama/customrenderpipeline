using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ScreenSpaceReflections
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField, Range(0.0f, 1.0f)] public float Intensity { get; private set; } = 1.0f;
        [field: SerializeField, Range(1, 128)] public int MaxSamples { get; private set; } = 32;
        [field: SerializeField, Range(0f, 10.0f), Tooltip("Thickness of a Depth Buffer Sample in world units")] public float Thickness { get; private set; } = 1.0f;
        [field: SerializeField, Range(0, 32)] public int ResolveSamples { get; private set; } = 8;
        [field: SerializeField, Min(0.0f)] public float ResolveSize { get; private set; } = 16.0f;
        [field: SerializeField] public bool UseRaytracing { get; private set; } = true;
    }

    private Material material;
    private RenderGraph renderGraph;
    private Settings settings;

    private PersistentRTHandleCache temporalCache;
    private RayTracingShader raytracingShader;

    public ScreenSpaceReflections(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        this.settings = settings;

        material = new Material(Shader.Find("Hidden/ScreenSpaceReflections")) { hideFlags = HideFlags.HideAndDontSave };
        temporalCache = new PersistentRTHandleCache(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Screen Space Reflections");
        raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Specular");
    }

    public void Render(RTHandle depth, RTHandle hiZDepth, RTHandle previousFrameColor, RTHandle normalRoughness, Camera camera, ICommonPassData commonPassData, int width, int height, RTHandle velocity, RTHandle bentNormalOcclusion, RTHandle albedoMetallic, float bias, float distantBias)
    {
        // Must be screen texture since we use stencil to skip sky pixels
        var tempResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
        var hitResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);

        if (settings.UseRaytracing)
        {
            // Need to set some things as globals so that hit shaders can access them..
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Specular GI Raytrace Setup"))
            {
                pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TerrainRenderData>(true);
                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                });
            }

            using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Specular GI Raytrace"))
            {
                var raytracingData = renderGraph.ResourceMap.GetRenderPassData<RaytracingResult>();

                pass.Initialize(raytracingShader, "RayGeneration", "RayTracing", raytracingData.Rtas, width, height, 1, bias, distantBias);
                pass.WriteTexture(tempResult, "HitColor");
                pass.WriteTexture(hitResult, "HitResult");
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("PreviousFrame", previousFrameColor); // Temporary, cuz of leaks if we don't use it..
                commonPassData.SetInputs(pass);

                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                });
            }
        }
        else
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Reflections Trace"))
            {
                pass.Initialize(material, camera: camera);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(tempResult, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(hitResult, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("PreviousFrame", previousFrameColor);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_HiZDepth", hiZDepth);
                pass.ReadTexture("Velocity", velocity);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);

                commonPassData.SetInputs(pass);
                pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();

                var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_Intensity", settings.Intensity);
                    pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                    pass.SetFloat(command, "_Thickness", settings.Thickness);
                    pass.SetFloat(command, "_MaxMip", Texture2DExtensions.MipCount(width, height) - 1);
                    pass.SetVector(command, "_PreviousColorScaleLimit", previousFrameColor.ScaleLimit2D);
                    commonPassData.SetProperties(pass, command);
                });
            }
        }

        var spatialResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
        var rayDepth = renderGraph.GetTexture(width, height, GraphicsFormat.R16_SFloat, isScreenTexture: true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Specular GI Spatial"))
        {
            pass.Initialize(material, 1, camera: camera);
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(rayDepth, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_Input", tempResult);
            pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("Velocity", velocity);
            pass.ReadTexture("_HitResult", hitResult);
            pass.ReadTexture("_NormalRoughness", normalRoughness);
            pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
            pass.ReadTexture("AlbedoMetallic", albedoMetallic);

            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
                pass.SetFloat(command, "_Intensity", settings.Intensity);
                pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                pass.SetFloat(command, "_Thickness", settings.Thickness);
                pass.SetInt(command, "_ResolveSamples", settings.ResolveSamples);
                pass.SetFloat(command, "_ResolveSize", settings.ResolveSize);
            });
        }

        var temporalResult = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
        var (current, history, wasCreated) = temporalCache.GetTextures(width, height, camera, true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Reflections Temporal"))
        {
            pass.Initialize(material, 2, camera: camera);
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(current, RenderBufferLoadAction.DontCare);
            pass.WriteTexture(temporalResult, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_Input", spatialResult);
            pass.ReadTexture("_History", history);
            pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("Velocity", velocity);
            pass.ReadTexture("_NormalRoughness", normalRoughness);
            pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
            pass.ReadTexture("AlbedoMetallic", albedoMetallic);
            pass.ReadTexture("RayDepth", rayDepth);

            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();
            pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
            pass.AddRenderPassData<LitData.Result>();

            var data = pass.SetRenderFunction<PassData>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
                pass.SetFloat(command, "_IsFirst", wasCreated ? 1.0f : 0.0f);
                pass.SetVector(command, "_HistoryScaleLimit", history.ScaleLimit2D);
                pass.SetFloat(command, "_Intensity", settings.Intensity);
            });
        }

        renderGraph.ResourceMap.SetRenderPassData(new ScreenSpaceReflectionResult(temporalResult));
    }

    class PassData
    {
    }
}

public struct ScreenSpaceReflectionResult : IRenderPassData
{
    public RTHandle ScreenSpaceReflections { get; }

    public ScreenSpaceReflectionResult(RTHandle screenSpaceReflections)
    {
        ScreenSpaceReflections = screenSpaceReflections ?? throw new ArgumentNullException(nameof(screenSpaceReflections));
    }

    public void SetInputs(RenderPass pass)
    {
        pass.ReadTexture("ScreenSpaceReflections", ScreenSpaceReflections);
    }

    public void SetProperties(RenderPass pass, CommandBuffer command)
    {
        pass.SetVector(command, "ScreenSpaceReflectionsScaleLimit", ScreenSpaceReflections.ScaleLimit2D);
    }
}