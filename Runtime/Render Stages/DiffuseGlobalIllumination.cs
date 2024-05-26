using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class DiffuseGlobalIllumination
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField, Range(0.0f, 1.0f)] public float Intensity { get; private set; } = 1.0f;
        [field: SerializeField, Range(1, 128)] public int MaxSamples { get; private set; } = 32;
        [field: SerializeField, Range(0f, 1.0f)] public float Thickness { get; private set; } = 0.1f;
        [field: SerializeField, Range(0.0f, 179.0f)] public float ConeAngle { get; private set; } = (1.0f / Mathf.PI) * Mathf.Rad2Deg;
        [field: SerializeField, Range(0, 32)] public int ResolveSamples { get; private set; } = 8;
        [field: SerializeField, Min(0.0f)] public float ResolveSize { get; private set; } = 16.0f;
        [field: SerializeField] public bool UseRaytracing { get; private set; } = true;
    }

    private readonly RenderGraph renderGraph;
    private readonly Material material;
    private readonly Settings settings;

    private PersistentRTHandleCache temporalCache;
    private RayTracingShader raytracingShader;

    public DiffuseGlobalIllumination(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        material = new Material(Shader.Find("Hidden/ScreenSpaceGlobalIllumination")) { hideFlags = HideFlags.HideAndDontSave };
        this.settings = settings;

        temporalCache = new PersistentRTHandleCache(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Screen Space Reflections");
        raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Diffuse");
    }

    public void Render(RTHandle depth, int width, int height, ICommonPassData commonPassData, Camera camera, RTHandle previousFrame, RTHandle velocity, RTHandle normalRoughness, RTHandle hiZDepth, RTHandle bentNormalOcclusion, float bias, float distantBias)
    {
        var tempResult = renderGraph.GetTexture(width, height, GraphicsFormat.B10G11R11_UFloatPack32, isScreenTexture: true);
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
                pass.AddRenderPassData<VolumetricClouds.CloudShadowDataResult>();
                pass.AddRenderPassData<ShadowRenderer.Result>(); 
                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                });
            }

            using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Diffuse GI Raytrace"))
            {
                var raytracingData = renderGraph.ResourceMap.GetRenderPassData<RaytracingResult>();

                pass.Initialize(raytracingShader, "RayGeneration", "RayTracing", raytracingData.Rtas, width, height, 1, bias, distantBias);
                pass.WriteTexture(tempResult, "HitColor");
                pass.WriteTexture(hitResult, "HitResult");
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("PreviousFrame", previousFrame); // Temporary, cuz of leaks if we don't use it..
                pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<Data>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                });
            }
        }
        else
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Trace"))
            {
                pass.Initialize(material, 0, 1, null, camera);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(tempResult);
                pass.WriteTexture(hitResult);

                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();
                pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();

                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("PreviousFrame", previousFrame);
                pass.ReadTexture("Velocity", velocity);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("_HiZDepth", hiZDepth);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);

                commonPassData.SetInputs(pass);

                var data = pass.SetRenderFunction<Data>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_Intensity", settings.Intensity);
                    pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                    pass.SetFloat(command, "_Thickness", settings.Thickness);
                    pass.SetFloat(command, "_MaxMip", Texture2DExtensions.MipCount(width, height) - 1);
                    pass.SetVector(command, "_PreviousColorScaleLimit", previousFrame.ScaleLimit2D);

                    var tanHalfFov = Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad);
                    pass.SetFloat(command, "_ConeAngle", Mathf.Tan(0.5f * settings.ConeAngle * Mathf.Deg2Rad) * (height / tanHalfFov * 0.5f));

                    commonPassData.SetProperties(pass, command);
                });
            }
        }

        var spatialResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
        var rayDepth = renderGraph.GetTexture(width, height, GraphicsFormat.R16_SFloat, isScreenTexture: true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Spatial"))
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

            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();

            var data = pass.SetRenderFunction<Data>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
                pass.SetFloat(command, "_Intensity", settings.Intensity);
                pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                pass.SetFloat(command, "_Thickness", settings.Thickness);
                pass.SetInt(command, "_ResolveSamples", settings.ResolveSamples);
                pass.SetFloat(command, "_ResolveSize", settings.ResolveSize);
            });
        }

        // Write final temporal result out to rgba16 (color+weight) and rgb111110 for final ambient composition
        var (current, history, wasCreated) = temporalCache.GetTextures(width, height, camera, true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Temporal"))
        {
            pass.Initialize(material, 2, camera: camera);
            pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_Input", spatialResult);
            pass.ReadTexture("_History", history);
            pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
            pass.ReadTexture("_Depth", depth);
            pass.ReadTexture("Velocity", velocity);
            pass.ReadTexture("_HitResult", hitResult);
            pass.ReadTexture("_NormalRoughness", normalRoughness);
            pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
            pass.ReadTexture("RayDepth", rayDepth);

            commonPassData.SetInputs(pass);
            pass.AddRenderPassData<TemporalAA.TemporalAAData>();
            pass.AddRenderPassData<AutoExposure.AutoExposureData>();
            pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
            pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();

            var data = pass.SetRenderFunction<Data>((command, pass, data) =>
            {
                commonPassData.SetProperties(pass, command);
                pass.SetFloat(command, "_IsFirst", wasCreated ? 1.0f : 0.0f);
                pass.SetVector(command, "_HistoryScaleLimit", history.ScaleLimit2D);
                pass.SetFloat(command, "_Intensity", settings.Intensity);
                pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                pass.SetFloat(command, "_Thickness", settings.Thickness);
            });
        }

        renderGraph.ResourceMap.SetRenderPassData(new Result(current, settings.Intensity));
    }

    private class Data
    {
    }

    public struct Result : IRenderPassData
    {
        public RTHandle ScreenSpaceGlobalIllumination { get; }
        private float intensity;

        public Result(RTHandle screenSpaceGlobalIllumination, float intensity)
        {
            ScreenSpaceGlobalIllumination = screenSpaceGlobalIllumination;
            this.intensity = intensity;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("ScreenSpaceGlobalIllumination", ScreenSpaceGlobalIllumination);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector(command, "ScreenSpaceGlobalIlluminationScaleLimit", ScreenSpaceGlobalIllumination.ScaleLimit2D);
            pass.SetFloat(command, "DiffuseGiStrength", intensity);
        }
    }
}
