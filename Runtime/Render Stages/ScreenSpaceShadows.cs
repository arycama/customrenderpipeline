using Arycama.CustomRenderPipeline;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class ScreenSpaceShadows : RenderFeature<(RTHandle depth, int width, int height, Camera camera, float bias, float distantBias, RTHandle normalRoughness)>
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField] public bool UseRaytracing { get; private set; } = true;
        [field: SerializeField, Range(0.0f, 180.0f)] public float LightAngularDiameter { get; private set; } = 0.5f;
        [field: SerializeField, Range(0.0f, 1.0f)] public float Intensity { get; private set; } = 1.0f;
        [field: SerializeField, Range(1, 128)] public int MaxSamples { get; private set; } = 32;
        [field: SerializeField, Range(0f, 1.0f)] public float Thickness { get; private set; } = 0.1f;
        [field: SerializeField, Range(0, 32)] public int ResolveSamples { get; private set; } = 8;
        [field: SerializeField, Min(0.0f)] public float ResolveSize { get; private set; } = 16.0f;
    }

    private readonly Material material;
    private readonly Settings settings;
    private readonly RayTracingShader shadowRaytracingShader;
    private readonly PersistentRTHandleCache temporalCache;

    public ScreenSpaceShadows(RenderGraph renderGraph, Settings settings) : base(renderGraph)
    {
        material = new Material(Shader.Find("Hidden/ScreenSpaceShadows")) { hideFlags = HideFlags.HideAndDontSave };
        this.settings = settings;
        shadowRaytracingShader = Resources.Load<RayTracingShader>("Raytracing/Shadow");
        temporalCache = new PersistentRTHandleCache(GraphicsFormat.R16_UNorm, renderGraph, "Screen Space Shadows");
    }

    protected override void Cleanup(bool disposing)
    {
        temporalCache.Dispose();
    }


    public override void Render((RTHandle depth, int width, int height, Camera camera, float bias, float distantBias, RTHandle normalRoughness) data)
    {
        var cullingResultsData = renderGraph.GetResource<CullingResultsData>();
        var cullingResults = cullingResultsData.CullingResults;

        var lightDirection = Vector3.up;
        for (var i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            var light = cullingResults.visibleLights[i];
            if (light.lightType != LightType.Directional)
                continue;

            lightDirection = -light.localToWorldMatrix.Forward();
            break;
        }

        var tempResult = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.R16G16B16A16_SFloat);

        if (settings.UseRaytracing)
        {
            // Need to set some things as globals so that hit shaders can access them..
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Raytraced Shadows Setup"))
            {
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TerrainRenderData>(true);
                pass.AddRenderPassData<CloudShadowDataResult>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<ICommonPassData>();
            }

            using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Raytraced Shadows"))
            {
                var raytracingData = renderGraph.GetResource<RaytracingResult>();

                pass.Initialize(shadowRaytracingShader, "RayGeneration", "RayTracingAmbientOcclusion", raytracingData.Rtas, data.width, data.height, 1, data.bias, data.distantBias, data.camera.fieldOfView);
                pass.WriteTexture(tempResult, "HitResult");
                pass.ReadTexture("_Depth", data.depth);
                pass.ReadTexture("_NormalRoughness", data.normalRoughness);
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetVector("LightDirection", lightDirection);
                    pass.SetFloat("LightCosTheta", Mathf.Cos(settings.LightAngularDiameter * Mathf.Deg2Rad * 0.5f));
                });
            }
        }
        else
        {
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Shadows"))
            {
                pass.Initialize(material, 0, 1);
                pass.WriteTexture(tempResult, RenderBufferLoadAction.DontCare);

                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<TemporalAAData>();

                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<LightingSetup.Result>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TerrainRenderData>(true);
                pass.AddRenderPassData<CloudShadowDataResult>();
                pass.AddRenderPassData<ShadowRenderer.Result>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<HiZMinDepthData>();

                pass.ReadTexture("_Depth", data.depth);
                pass.ReadTexture("_NormalRoughness", data.normalRoughness);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetVector("LightDirection", lightDirection);
                    pass.SetFloat("_MaxSteps", settings.MaxSamples);
                    pass.SetFloat("_Thickness", settings.Thickness);
                    pass.SetFloat("_Intensity", settings.Intensity);
                    pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(data.width, data.height) - 1);
                    pass.SetFloat("LightCosTheta", Mathf.Cos(settings.LightAngularDiameter * Mathf.Deg2Rad * 0.5f));
                });
            }
        }

        var spatialResult = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.R16_UNorm, isScreenTexture: true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Shadows Spatial"))
        {
            pass.Initialize(material, 1);
            pass.WriteDepth(data.depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_Input", tempResult);
            pass.ReadTexture("_Stencil", data.depth, subElement: RenderTextureSubElement.Stencil);
            pass.ReadTexture("_Depth", data.depth);

            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<SkyReflectionAmbientData>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<ICommonPassData>();
            pass.AddRenderPassData<VelocityData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_Intensity", settings.Intensity);
                pass.SetFloat("_MaxSteps", settings.MaxSamples);
                pass.SetFloat("_Thickness", settings.Thickness);
                pass.SetInt("_ResolveSamples", settings.ResolveSamples);
                pass.SetFloat("_ResolveSize", settings.ResolveSize);
                pass.SetFloat("DiffuseGiStrength", settings.Intensity);

                var lightCosTheta = Mathf.Cos(settings.LightAngularDiameter * Mathf.Deg2Rad * 0.5f);
                pass.SetFloat("LightCosTheta", lightCosTheta);
                pass.SetVector("LightDirection", lightDirection);
            });
        }

        // Write final temporal result out to rgba16 (color+weight) and rgb111110 for final ambient composition
        var (current, history, wasCreated) = temporalCache.GetTextures(data.width, data.height, data.camera, true);
        using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Shadows Temporal"))
        {
            pass.Initialize(material, 2);
            pass.WriteDepth(data.depth, RenderTargetFlags.ReadOnlyDepthStencil);
            pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

            pass.ReadTexture("_TemporalInput", spatialResult);
            pass.ReadTexture("_History", history);
            pass.ReadTexture("_Stencil", data.depth, subElement: RenderTextureSubElement.Stencil);
            pass.ReadTexture("_Depth", data.depth);
            //pass.ReadTexture("_HitResult", hitResult);
            pass.ReadTexture("_NormalRoughness", data.normalRoughness);
            //pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
            //pass.ReadTexture("RayDepth", rayDepth);

            pass.AddRenderPassData<TemporalAAData>();
            pass.AddRenderPassData<AutoExposureData>();
            pass.AddRenderPassData<SkyReflectionAmbientData>();
            pass.AddRenderPassData<AtmospherePropertiesAndTables>();
            pass.AddRenderPassData<ICommonPassData>();
            pass.AddRenderPassData<VelocityData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetFloat("_IsFirst", wasCreated ? 1.0f : 0.0f);
                pass.SetVector("_HistoryScaleLimit", history.ScaleLimit2D);
                pass.SetFloat("_Intensity", settings.Intensity);
                pass.SetFloat("_MaxSteps", settings.MaxSamples);
                pass.SetFloat("_Thickness", settings.Thickness);
            });
        }

        renderGraph.SetResource(new Result(current));;
    }

    public struct Result : IRenderPassData
    {
        public RTHandle ScreenSpaceShadows { get; }

        public Result(RTHandle screenSpaceShadows)
        {
            ScreenSpaceShadows = screenSpaceShadows;
        }

        public void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("ScreenSpaceShadows", ScreenSpaceShadows);
        }

        public void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector("ScreenSpaceShadowsScaleLimit", ScreenSpaceShadows.ScaleLimit2D);
        }
    }
}
