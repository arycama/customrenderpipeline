using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class AmbientOcclusion : RenderFeature<(Camera camera, RTHandle depth, float scale, RTHandle normal, RTHandle bentNormalOcclusion, float bias, float distantBias)>
    {
        private readonly Settings settings;
        private readonly Material material;
        private readonly RayTracingShader ambientOcclusionRaytracingShader;

        private readonly PersistentRTHandleCache temporalCache;

        public AmbientOcclusion(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Ambient Occlusion")) { hideFlags = HideFlags.HideAndDontSave };
            temporalCache = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Ambient Occlusion");
            ambientOcclusionRaytracingShader = Resources.Load<RayTracingShader>("Raytracing/AmbientOcclusion");
        }

        protected override void Cleanup(bool disposing)
        {
            temporalCache.Dispose();
        }

        public override void Render((Camera camera, RTHandle depth, float scale, RTHandle normal, RTHandle bentNormalOcclusion, float bias, float distantBias) data)
        {
            var width = (int)(data.camera.pixelWidth * data.scale);
            var height = (int)(data.camera.pixelHeight * data.scale);

            var tempResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
            var hitResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);

            var tanHalfFov = Mathf.Tan(data.camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            var falloffStart = settings.Radius * settings.Falloff;
            var falloffEnd = settings.Radius;

            if (settings.UseRaytracing)
            {
                using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Raytraced Ambient Occlusion"))
                {
                    var raytracingData = renderGraph.GetResource<RaytracingResult>();

                    pass.Initialize(ambientOcclusionRaytracingShader, "RayGeneration", "RayTracingAmbientOcclusion", raytracingData.Rtas, width, height, 1, data.bias, data.distantBias, data.camera.fieldOfView);
                    pass.WriteTexture(tempResult, "HitColor");
                    pass.WriteTexture(hitResult, "HitResult");
                    pass.ReadTexture("_Depth", data.depth);
                    pass.ReadTexture("_NormalRoughness", data.normal);
                    pass.AddRenderPassData<ICommonPassData>();

                    pass.SetRenderFunction((
                        rawRadius: settings.Radius,
                        radius: height / tanHalfFov * 0.5f * settings.Radius,
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
                    pass.ReadTexture("_Depth", data.depth);
                    pass.ReadTexture("_NormalRoughness", data.normal);
                    pass.WriteDepth(data.depth, RenderTargetFlags.ReadOnlyDepthStencil);
                    pass.AddRenderPassData<ICommonPassData>();

                    pass.SetRenderFunction((
                         rawRadius: settings.Radius,
                         radius: height / tanHalfFov * 0.5f * settings.Radius,
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

            var spatialResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
            //var rayDepth = renderGraph.GetTexture(width, height, GraphicsFormat.R16_SFloat, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Spatial"))
            {
                pass.Initialize(material, 1);
                pass.WriteDepth(data.depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);
                //pass.WriteTexture(rayDepth, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", tempResult);
                pass.ReadTexture("_HitResult", hitResult);
                pass.ReadTexture("_Stencil", data.depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", data.depth);
                pass.ReadTexture("_NormalRoughness", data.normal);
                pass.ReadTexture("_BentNormalOcclusion", data.bentNormalOcclusion);

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

            var (current, history, wasCreated) = temporalCache.GetTextures(width, height, data.camera, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Temporal"))
            {
                pass.Initialize(material, 2);
                pass.WriteDepth(data.depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", spatialResult);
                pass.ReadTexture("_History", history);
                pass.ReadTexture("_Stencil", data.depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", data.depth);

                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_IsFirst", wasCreated ? 1.0f : 0.0f);
                    pass.SetVector("_HistoryScaleLimit", history.ScaleLimit2D);
                });
            }

            renderGraph.SetResource(new Result(current));;

            var newBentNormalOcclusion = renderGraph.GetTexture(width, height, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Resolve"))
            {
                pass.Initialize(material, 3);
                pass.WriteDepth(data.depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(newBentNormalOcclusion, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", current);
                pass.ReadTexture("_BentNormalOcclusion", data.bentNormalOcclusion);
                pass.AddRenderPassData<TemporalAAData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetFloat("_AoStrength", settings.Strength);
                    pass.SetVector("InputScaleLimit", current.ScaleLimit2D);

                });
            }

            renderGraph.SetResource(new BentNormalOcclusionData(newBentNormalOcclusion));;
        }

        // Only used for debugging as the result is combined into the bent normal texture
        public readonly struct Result : IRenderPassData
        {
            public RTHandle AmbientOcclusion { get; }

            public Result(RTHandle ambientOcclusion)
            {
                AmbientOcclusion = ambientOcclusion ?? throw new ArgumentNullException(nameof(ambientOcclusion));
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