using Arycama.CustomRenderPipeline.Water;
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public partial class ScreenSpaceReflections : RenderFeature
    {
        private readonly Material material;
        private readonly Settings settings;

        private readonly PersistentRTHandleCache temporalCache;
        private readonly RayTracingShader raytracingShader;

        public ScreenSpaceReflections(RenderGraph renderGraph, Settings settings) : base(renderGraph)
        {
            this.settings = settings;

            material = new Material(Shader.Find("Hidden/ScreenSpaceReflections")) { hideFlags = HideFlags.HideAndDontSave };
            temporalCache = new PersistentRTHandleCache(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Screen Space Reflections");
            raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Specular");
        }

        protected override void Cleanup(bool disposing)
        {
            temporalCache.Dispose();
        }

        public override void Render()
        {
            var viewData = renderGraph.GetResource<ViewData>();

            // Must be screen texture since we use stencil to skip sky pixels
            var tempResult = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);

            // Slight fuzzyness with 16 bits, probably due to depth.. would like to investigate
            var hitResult = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R32G32B32A32_SFloat, isScreenTexture: true);

            var depth = renderGraph.GetResource<CameraDepthData>().Handle;
            var normalRoughness = renderGraph.GetResource<NormalRoughnessData>().Handle;
            var previousFrame = renderGraph.GetResource<PreviousFrameColorData>().Handle;
            var albedoMetallic = renderGraph.GetResource<AlbedoMetallicData>().Handle;

            if (settings.UseRaytracing)
            {
                // Need to set some things as globals so that hit shaders can access them..
                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Specular GI Raytrace Setup"))
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

                using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Specular GI Raytrace"))
                {
                    var raytracingData = renderGraph.GetResource<RaytracingResult>();

                    pass.Initialize(raytracingShader, "RayGeneration", "RayTracing", raytracingData.Rtas, viewData.ScaledWidth, viewData.ScaledHeight, 1, raytracingData.Bias, raytracingData.DistantBias, viewData.FieldOfView);
                    pass.WriteTexture(tempResult, "HitColor");
                    pass.WriteTexture(hitResult, "HitResult");
                    pass.ReadTexture("_Depth", depth);
                    pass.ReadTexture("_NormalRoughness", normalRoughness);
                    pass.ReadTexture("PreviousFrame", previousFrame); // Temporary, cuz of leaks if we don't use it..

                    pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                    pass.AddRenderPassData<WaterPrepassResult>(true);
                    pass.AddRenderPassData<ICommonPassData>();
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
                    pass.ConfigureClear(RTClearFlags.Color);

                    pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
                    pass.ReadTexture("_NormalRoughness", normalRoughness);
                    pass.ReadTexture("PreviousFrame", previousFrame);
                    pass.ReadTexture("_Depth", depth);

                    pass.AddRenderPassData<SkyReflectionAmbientData>();
                    pass.AddRenderPassData<LitData.Result>();
                    pass.AddRenderPassData<TemporalAAData>();
                    pass.AddRenderPassData<AutoExposureData>();
                    pass.AddRenderPassData<ICommonPassData>();
                    pass.AddRenderPassData<BentNormalOcclusionData>();
                    pass.AddRenderPassData<VelocityData>();
                    pass.AddRenderPassData<HiZMinDepthData>();

                    pass.SetRenderFunction((command, pass) =>
                    {
                        pass.SetFloat("_MaxSteps", settings.MaxSamples);
                        pass.SetFloat("_Thickness", settings.Thickness);
                        pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(viewData.ScaledWidth, viewData.ScaledHeight) - 1);
                        pass.SetVector("_PreviousColorScaleLimit", pass.GetScaleLimit2D(previousFrame));
                    });
                }
            }

            var spatialResult = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
            var rayDepth = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R16_SFloat, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Specular GI Spatial"))
            {
                pass.Initialize(material, 1);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(rayDepth, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", tempResult);
                pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_HitResult", hitResult);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("AlbedoMetallic", albedoMetallic);

                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<WaterPrepassResult>(true);
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<BentNormalOcclusionData>();
                pass.AddRenderPassData<VelocityData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("_ResolveSamples", settings.ResolveSamples);
                    pass.SetFloat("_ResolveSize", settings.ResolveSize);
                    pass.SetFloat("SpecularGiStrength", settings.Intensity);
                });
            }

            var (current, history, wasCreated) = temporalCache.GetTextures(viewData.ScaledWidth, viewData.ScaledHeight, viewData.ViewIndex, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Reflections Temporal"))
            {
                pass.Initialize(material, 2);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_TemporalInput", spatialResult);
                pass.ReadTexture("_History", history);
                pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("AlbedoMetallic", albedoMetallic);
                pass.ReadTexture("RayDepth", rayDepth);

                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<LitData.Result>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<BentNormalOcclusionData>();
                pass.AddRenderPassData<VelocityData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_IsFirst", wasCreated ? 1.0f : 0.0f);
                    pass.SetVector("_HistoryScaleLimit", pass.GetScaleLimit2D(history));
                });
            }

            renderGraph.SetResource(new ScreenSpaceReflectionResult(current, settings.Intensity)); ;
        }
    }

    public readonly struct ScreenSpaceReflectionResult : IRenderPassData
    {
        public RTHandle ScreenSpaceReflections { get; }
        private readonly float intensity;

        public ScreenSpaceReflectionResult(RTHandle screenSpaceReflections, float intensity)
        {
            ScreenSpaceReflections = screenSpaceReflections ?? throw new ArgumentNullException(nameof(screenSpaceReflections));
            this.intensity = intensity;
        }

        public readonly void SetInputs(RenderPass pass)
        {
            pass.ReadTexture("ScreenSpaceReflections", ScreenSpaceReflections);
        }

        public readonly void SetProperties(RenderPass pass, CommandBuffer command)
        {
            pass.SetVector("ScreenSpaceReflectionsScaleLimit", pass.GetScaleLimit2D(ScreenSpaceReflections));
            pass.SetFloat("SpecularGiStrength", intensity);
        }
    }
}