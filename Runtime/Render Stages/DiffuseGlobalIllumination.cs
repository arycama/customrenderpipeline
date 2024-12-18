﻿using Arycama.CustomRenderPipeline;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public partial class DiffuseGlobalIllumination : RenderFeature<(RTHandle depth, int width, int height, Camera camera, RTHandle previousFrame, RTHandle normalRoughness, float bias, float distantBias)>
    {
        private readonly Material material;
        private readonly Settings settings;

        private readonly PersistentRTHandleCache temporalCache;
        private readonly RayTracingShader raytracingShader;

        public DiffuseGlobalIllumination(RenderGraph renderGraph, Settings settings) : base(renderGraph)
        {
            material = new Material(Shader.Find("Hidden/ScreenSpaceGlobalIllumination")) { hideFlags = HideFlags.HideAndDontSave };
            this.settings = settings;

            temporalCache = new PersistentRTHandleCache(GraphicsFormat.A2B10G10R10_UNormPack32, renderGraph, "Screen Space Diffuse");
            raytracingShader = Resources.Load<RayTracingShader>("Raytracing/Diffuse");
        }

        protected override void Cleanup(bool disposing)
        {
            temporalCache.Dispose();
        }

        public override void Render((RTHandle depth, int width, int height, Camera camera, RTHandle previousFrame, RTHandle normalRoughness, float bias, float distantBias) data)
        {
            var tempResult = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
            var hitResult = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);

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

                using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Diffuse GI Raytrace"))
                {
                    var raytracingData = renderGraph.GetResource<RaytracingResult>();

                    pass.Initialize(raytracingShader, "RayGeneration", "RayTracing", raytracingData.Rtas, data.width, data.height, 1, data.bias, data.distantBias, data.camera.fieldOfView);
                    pass.WriteTexture(tempResult, "HitColor");
                    pass.WriteTexture(hitResult, "HitResult");
                    pass.ReadTexture("_Depth", data.depth);
                    pass.ReadTexture("_NormalRoughness", data.normalRoughness);
                    pass.ReadTexture("PreviousFrame", data.previousFrame); // Temporary, cuz of leaks if we don't use it..
                    pass.AddRenderPassData<SkyReflectionAmbientData>();
                    pass.AddRenderPassData<LightingSetup.Result>();
                    pass.AddRenderPassData<AutoExposureData>();
                    pass.AddRenderPassData<ICommonPassData>();
                }
            }
            else
            {
                using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Trace"))
                {
                    pass.Initialize(material);
                    pass.WriteDepth(data.depth, RenderTargetFlags.ReadOnlyDepthStencil);
                    pass.WriteTexture(tempResult);
                    pass.WriteTexture(hitResult);
                    pass.ConfigureClear(RTClearFlags.Color);

                    pass.AddRenderPassData<LightingSetup.Result>();
                    pass.AddRenderPassData<TemporalAAData>();
                    pass.AddRenderPassData<AutoExposureData>();
                    pass.AddRenderPassData<SkyReflectionAmbientData>();
                    pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                    pass.AddRenderPassData<ICommonPassData>();
                    pass.AddRenderPassData<BentNormalOcclusionData>();
                    pass.AddRenderPassData<VelocityData>();
                    pass.AddRenderPassData<HiZMinDepthData>();

                    pass.ReadTexture("_Depth", data.depth);
                    pass.ReadTexture("PreviousFrame", data.previousFrame);
                    pass.ReadTexture("_NormalRoughness", data.normalRoughness);

                    pass.SetRenderFunction((command, pass) =>
                    {
                        pass.SetFloat("_Intensity", settings.Intensity);
                        pass.SetFloat("_MaxSteps", settings.MaxSamples);
                        pass.SetFloat("_Thickness", settings.Thickness);
                        pass.SetFloat("_MaxMip", Texture2DExtensions.MipCount(data.width, data.height) - 1);
                        pass.SetVector("_PreviousColorScaleLimit", data.previousFrame.ScaleLimit2D);

                        var tanHalfFov = Mathf.Tan(0.5f * data.camera.fieldOfView * Mathf.Deg2Rad);
                        pass.SetFloat("_ConeAngle", Mathf.Tan(0.5f * settings.ConeAngle * Mathf.Deg2Rad) * (data.height / tanHalfFov * 0.5f));
                    });
                }
            }

            var spatialResult = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.A2B10G10R10_UNormPack32, isScreenTexture: true);
            var rayDepth = renderGraph.GetTexture(data.width, data.height, GraphicsFormat.R16_SFloat, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Spatial"))
            {
                pass.Initialize(material, 1);
                pass.WriteDepth(data.depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);
                pass.WriteTexture(rayDepth, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", tempResult);
                pass.ReadTexture("_Stencil", data.depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", data.depth);
                pass.ReadTexture("_HitResult", hitResult);
                pass.ReadTexture("_NormalRoughness", data.normalRoughness);

                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<BentNormalOcclusionData>();
                pass.AddRenderPassData<VelocityData>();

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

            // Write final temporal result out to rgba16 (color+weight) and rgb111110 for final ambient composition
            var (current, history, wasCreated) = temporalCache.GetTextures(data.width, data.height, data.camera, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Temporal"))
            {
                pass.Initialize(material, 2);
                pass.WriteDepth(data.depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_TemporalInput", spatialResult);
                pass.ReadTexture("_History", history);
                pass.ReadTexture("_Stencil", data.depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", data.depth);
                pass.ReadTexture("_HitResult", hitResult);
                pass.ReadTexture("_NormalRoughness", data.normalRoughness);
                pass.ReadTexture("RayDepth", rayDepth);

                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<BentNormalOcclusionData>();
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

            renderGraph.SetResource(new Result(current, settings.Intensity));;
        }

        public readonly struct Result : IRenderPassData
        {
            public RTHandle ScreenSpaceGlobalIllumination { get; }
            private readonly float intensity;

            public Result(RTHandle screenSpaceGlobalIllumination, float intensity)
            {
                ScreenSpaceGlobalIllumination = screenSpaceGlobalIllumination;
                this.intensity = intensity;
            }

            public readonly void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("ScreenSpaceGlobalIllumination", ScreenSpaceGlobalIllumination);
            }

            public readonly void SetProperties(RenderPass pass, CommandBuffer command)
            {
                pass.SetVector("ScreenSpaceGlobalIlluminationScaleLimit", ScreenSpaceGlobalIllumination.ScaleLimit2D);
                pass.SetFloat("DiffuseGiStrength", intensity);
            }
        }
    }
}