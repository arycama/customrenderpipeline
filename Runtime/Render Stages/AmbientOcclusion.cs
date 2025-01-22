using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class AmbientOcclusion : RenderFeature
    {
        private readonly Settings settings;
        private readonly Material material;
        private readonly RayTracingShader ambientOcclusionRaytracingShader;

        private readonly PersistentRTHandleCache temporalCache;

        public AmbientOcclusion(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Ambient Occlusion")) { hideFlags = HideFlags.HideAndDontSave };
            temporalCache = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Ambient Occlusion", isScreenTexture: true);
            ambientOcclusionRaytracingShader = Resources.Load<RayTracingShader>("Raytracing/AmbientOcclusion");
        }

        protected override void Cleanup(bool disposing)
        {
            temporalCache.Dispose();
        }

        public override void Render()
        {
            var viewData = renderGraph.GetResource<ViewData>();
            var depth = renderGraph.GetResource<CameraDepthData>().Handle;
            var normalRoughness = renderGraph.GetResource<NormalRoughnessData>().Handle;
            var bentNormalOcclusion = renderGraph.GetResource<BentNormalOcclusionData>().Handle;

            var tempResult = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
            var hitResult = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);

            var tanHalfFov = Mathf.Tan(viewData.FieldOfView * Mathf.Deg2Rad * 0.5f);
            var falloffStart = settings.Radius * settings.Falloff;
            var falloffEnd = settings.Radius;

            if (settings.UseRaytracing)
            {
                using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Raytraced Ambient Occlusion"))
                {
                    var raytracingData = renderGraph.GetResource<RaytracingResult>();

                    pass.Initialize(ambientOcclusionRaytracingShader, "RayGeneration", "RayTracingAmbientOcclusion", raytracingData.Rtas, viewData.ScaledWidth, viewData.ScaledHeight, 1, raytracingData.Bias, raytracingData.DistantBias, viewData.FieldOfView);
                    pass.WriteTexture(tempResult, "HitColor");
                    pass.WriteTexture(hitResult, "HitResult");
                    pass.ReadTexture("_Depth", depth);
                    pass.ReadTexture("_NormalRoughness", normalRoughness);
                    pass.AddRenderPassData<ICommonPassData>();

                    pass.SetRenderFunction((
                        rawRadius: settings.Radius,
                        radius: viewData.ScaledHeight / tanHalfFov * 0.5f * settings.Radius,
                        aoStrength: settings.Strength,
                        falloffScale: settings.Falloff == 1.0f ? 0.0f : 1.0f / (falloffStart * falloffStart - falloffEnd * falloffEnd),
                        falloffBias: settings.Falloff == 1.0f ? 1.0f : 1.0f / (1.0f - settings.Falloff * settings.Falloff),
                        sampleCount: settings.SampleCount
                    ),

                    (command, pass, data) =>
                    {
                        pass.SetFloat("_Radius", data.radius);
                        pass.SetFloat("_RawRadius", data.rawRadius);
                        pass.SetFloat("_AoStrength", data.aoStrength);
                        pass.SetFloat("_FalloffScale", data.falloffScale);
                        pass.SetFloat("_FalloffBias", data.falloffBias);
                        pass.SetFloat("_SampleCount", data.sampleCount);
                    });
                }
            }
            else
            {
                using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Trace"))
                {
                    pass.Initialize(material);
                    pass.WriteTexture(tempResult);
                    pass.WriteTexture(hitResult);
                    pass.ReadTexture("_Depth", depth);
                    pass.ReadTexture("_NormalRoughness", normalRoughness);
                    pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                    pass.AddRenderPassData<ICommonPassData>();

                    pass.SetRenderFunction((
                         rawRadius: settings.Radius,
                         radius: viewData.ScaledHeight / tanHalfFov * 0.5f * settings.Radius,
                         aoStrength: settings.Strength,
                         falloffScale: settings.Falloff == 1.0f ? 0.0f : 1.0f / (falloffStart * falloffStart - falloffEnd * falloffEnd),
                         falloffBias: settings.Falloff == 1.0f ? 1.0f : 1.0f / (1.0f - settings.Falloff * settings.Falloff),
                         sampleCount: settings.SampleCount,
                         thinOccluderCompensation: settings.ThinOccluderCompensation
                     ),

                     (command, pass, data) =>
                     {
                         pass.SetFloat("_Radius", data.radius);
                         pass.SetFloat("_RawRadius", data.rawRadius);
                         pass.SetFloat("_AoStrength", data.aoStrength);
                         pass.SetFloat("_FalloffScale", data.falloffScale);
                         pass.SetFloat("_FalloffBias", data.falloffBias);
                         pass.SetFloat("_SampleCount", data.sampleCount);
                         pass.SetFloat("_ThinOccluderCompensation", data.thinOccluderCompensation);

                         var thinOccStart = settings.ThinOccluderStart * settings.Radius;
                         var thinOccEnd = settings.ThinOccluderEnd * settings.Radius;

                         pass.SetFloat("_ThinOccluderScale", settings.ThinOccluderFalloff / (0.5f * (thinOccEnd - thinOccStart)));
                         pass.SetFloat("_ThinOccluderOffset", settings.ThinOccluderFalloff * (thinOccStart + thinOccEnd) / (thinOccStart - thinOccEnd));
                         pass.SetFloat("_ThinOccluderFalloff", settings.ThinOccluderFalloff);
                     });
                }
            }

            var spatialResult = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
            //var rayDepth = renderGraph.GetTexture(width, height, GraphicsFormat.R16_SFloat, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Spatial"))
            {
                pass.Initialize(material, 1);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);
                //pass.WriteTexture(rayDepth, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", tempResult);
                pass.ReadTexture("_HitResult", hitResult);
                pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("_NormalRoughness", normalRoughness);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);

                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<SkyReflectionAmbientData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<VelocityData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("_ResolveSamples", settings.ResolveSamples);
                    pass.SetFloat("_ResolveSize", settings.ResolveSize);
                });
            }

            var (current, history, wasCreated) = temporalCache.GetTextures(viewData.ScaledWidth, viewData.ScaledHeight, viewData.ViewIndex);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Temporal"))
            {
                pass.Initialize(material, 2);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", spatialResult);
                pass.ReadTexture("_History", history);
                pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", depth);

                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_IsFirst", wasCreated ? 1.0f : 0.0f);
                    pass.SetVector("_HistoryScaleLimit", pass.GetScaleLimit2D(history));
                });
            }

            renderGraph.SetResource(new Result(current)); ;

            var newBentNormalOcclusion = renderGraph.GetTexture(viewData.ScaledWidth, viewData.ScaledHeight, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Resolve"))
            {
                pass.Initialize(material, 3);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(newBentNormalOcclusion, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", current);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
                pass.AddRenderPassData<TemporalAAData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_AoStrength", settings.Strength);
                    pass.SetVector("InputScaleLimit", pass.GetScaleLimit2D(current));

                });
            }

            renderGraph.SetResource(new BentNormalOcclusionData(newBentNormalOcclusion)); ;
        }

        // Only used for debugging as the result is combined into the bent normal texture
        public readonly struct Result : IRenderPassData
        {
            public ResourceHandle<RenderTexture> AmbientOcclusion { get; }

            public Result(ResourceHandle<RenderTexture> ambientOcclusion)
            {
                AmbientOcclusion = ambientOcclusion;
            }

            public readonly void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("AmbientOcclusion", AmbientOcclusion);
            }

            public readonly void SetProperties(RenderPass pass, CommandBuffer command)
            {
            }
        }

        [Serializable]
        public class Settings
        {
            [field: SerializeField, Range(0.0f, 8.0f)] public float Strength { get; private set; } = 1.0f;
            [field: SerializeField] public float Radius { get; private set; } = 5.0f;
            [field: SerializeField, Range(0f, 1f)] public float Falloff { get; private set; } = 0.75f;
            [field: SerializeField, Min(0.0f)] public float ThinOccluderCompensation { get; private set; } = 0.02f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float ThinOccluderStart { get; private set; } = 0.25f;
            [field: SerializeField, Range(0.0f, 1.0f)] public float ThinOccluderEnd { get; private set; } = 0.75f;
            [field: SerializeField, Min(0.0f)] public float ThinOccluderFalloff { get; private set; } = 2.0f;
            [field: SerializeField, Range(1, 32)] public int SampleCount { get; private set; } = 8;

            [field: SerializeField, Range(0, 32)] public int ResolveSamples { get; private set; } = 8;
            [field: SerializeField, Min(0.0f)] public float ResolveSize { get; private set; } = 16.0f;
            [field: SerializeField] public bool UseRaytracing { get; private set; } = false;
        }
    }
}