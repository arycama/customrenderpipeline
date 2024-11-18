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
            temporalCache = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Physical Sky");
            ambientOcclusionRaytracingShader = Resources.Load<RayTracingShader>("Raytracing/AmbientOcclusion");
        }

        public void Render(Camera camera, RTHandle depth, float scale, RTHandle normal, ICommonPassData commonPassData, RTHandle velocity, ref RTHandle bentNormalOcclusion, float bias, float distantBias)
        {
            var width = (int)(camera.pixelWidth * scale);
            var height = (int)(camera.pixelHeight * scale);

            var tempResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
            var hitResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);

            if (settings.UseRaytracing)
            {
                using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Raytraced Ambient Occlusion"))
                {
                    var raytracingData = renderGraph.ResourceMap.GetRenderPassData<RaytracingResult>(renderGraph.FrameIndex);

                    pass.Initialize(ambientOcclusionRaytracingShader, "RayGeneration", "RayTracingAmbientOcclusion", raytracingData.Rtas, width, height, 1, bias, distantBias, camera.fieldOfView);
                    pass.WriteTexture(tempResult, "HitColor");
                    pass.WriteTexture(hitResult, "HitResult");
                    pass.ReadTexture("_Depth", depth);
                    pass.ReadTexture("_NormalRoughness", normal);
                    commonPassData.SetInputs(pass);

                    var data = pass.SetRenderFunction<Pass1Data>((command, pass, data) =>
                    {
                        commonPassData.SetProperties(pass, command);

                        pass.SetFloat(command, "_Radius", data.radius);
                        pass.SetFloat(command, "_RawRadius", data.rawRadius);
                        pass.SetFloat(command, "_AoStrength", data.aoStrength);
                        pass.SetFloat(command, "_FalloffScale", data.falloffScale);
                        pass.SetFloat(command, "_FalloffBias", data.falloffBias);
                        pass.SetFloat(command, "_SampleCount", data.sampleCount);
                    });

                    var tanHalfFov = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    var falloffStart = settings.Radius * settings.Falloff;
                    var falloffEnd = settings.Radius;

                    data.rawRadius = settings.Radius;
                    data.radius = height / tanHalfFov * 0.5f * settings.Radius;
                    data.aoStrength = settings.Strength;
                    data.falloffScale = settings.Falloff == 1.0f ? 0.0f : 1.0f / (falloffStart * falloffStart - falloffEnd * falloffEnd);
                    data.falloffBias = settings.Falloff == 1.0f ? 1.0f : 1.0f / (1.0f - settings.Falloff * settings.Falloff);
                    data.sampleCount = settings.SampleCount;
                }
            }
            else
            {
                using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Trace"))
                {
                    pass.Initialize(material, 0, 1, null, camera);
                    pass.WriteTexture(tempResult);
                    pass.WriteTexture(hitResult);
                    pass.ReadTexture("_Depth", depth);
                    pass.ReadTexture("_NormalRoughness", normal);
                    pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                    commonPassData.SetInputs(pass);

                    var data = pass.SetRenderFunction<Pass1Data>((command, pass, data) =>
                    {
                        commonPassData.SetProperties(pass, command);

                        pass.SetFloat(command, "_Radius", data.radius);
                        pass.SetFloat(command, "_RawRadius", data.rawRadius);
                        pass.SetFloat(command, "_AoStrength", data.aoStrength);
                        pass.SetFloat(command, "_FalloffScale", data.falloffScale);
                        pass.SetFloat(command, "_FalloffBias", data.falloffBias);
                        pass.SetFloat(command, "_SampleCount", data.sampleCount);
                        pass.SetFloat(command, "_ThinOccluderCompensation", data.thinOccluderCompensation);

                        var thinOccStart = settings.ThinOccluderStart * settings.Radius;
                        var thinOccEnd = settings.ThinOccluderEnd * settings.Radius;

                        pass.SetFloat(command, "_ThinOccluderScale", settings.ThinOccluderFalloff / (0.5f * (thinOccEnd - thinOccStart)));
                        pass.SetFloat(command, "_ThinOccluderOffset", settings.ThinOccluderFalloff * (thinOccStart + thinOccEnd) / (thinOccStart - thinOccEnd));
                        pass.SetFloat(command, "_ThinOccluderFalloff", settings.ThinOccluderFalloff);
                    });

                    var tanHalfFov = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    var falloffStart = settings.Radius * settings.Falloff;
                    var falloffEnd = settings.Radius;

                    data.rawRadius = settings.Radius;
                    data.radius = height / tanHalfFov * 0.5f * settings.Radius;
                    data.aoStrength = settings.Strength;
                    data.falloffScale = settings.Falloff == 1.0f ? 0.0f : 1.0f / (falloffStart * falloffStart - falloffEnd * falloffEnd);
                    data.falloffBias = settings.Falloff == 1.0f ? 1.0f : 1.0f / (1.0f - settings.Falloff * settings.Falloff);
                    data.sampleCount = settings.SampleCount;
                    data.thinOccluderCompensation = settings.ThinOccluderCompensation;
                }
            }

            var spatialResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
            //var rayDepth = renderGraph.GetTexture(width, height, GraphicsFormat.R16_SFloat, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Screen Space Global Illumination Spatial"))
            {
                pass.Initialize(material, 1, camera: camera);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(spatialResult, RenderBufferLoadAction.DontCare);
                //pass.WriteTexture(rayDepth, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", tempResult);
                pass.ReadTexture("_HitResult", hitResult);
                pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("Velocity", velocity);
                pass.ReadTexture("_NormalRoughness", normal);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);

                commonPassData.SetInputs(pass);
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();
                pass.AddRenderPassData<PhysicalSky.ReflectionAmbientData>();
                pass.AddRenderPassData<PhysicalSky.AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<AutoExposure.AutoExposureData>();

                var data = pass.SetRenderFunction<EmptyPassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                    //pass.SetFloat(command, "_Intensity", settings.Strength);
                    //pass.SetFloat(command, "_MaxSteps", settings.MaxSamples);
                    //pass.SetFloat(command, "_Thickness", settings.Thickness);
                    pass.SetInt(command, "_ResolveSamples", settings.ResolveSamples);
                    pass.SetFloat(command, "_ResolveSize", settings.ResolveSize);
                });
            }

            var (current, history, wasCreated) = temporalCache.GetTextures(width, height, camera, true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Temporal"))
            {
                pass.Initialize(material, 2, camera: camera);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", spatialResult);
                pass.ReadTexture("_History", history);
                pass.ReadTexture("_Stencil", depth, subElement: RenderTextureSubElement.Stencil);
                pass.ReadTexture("_Depth", depth);
                pass.ReadTexture("Velocity", velocity);

                commonPassData.SetInputs(pass);
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();

                var data = pass.SetRenderFunction<TemporalPassData>((command, pass, data) =>
                {
                    commonPassData.SetProperties(pass, command);
                    pass.SetFloat(command, "_IsFirst", wasCreated ? 1.0f : 0.0f);
                    pass.SetVector(command, "_HistoryScaleLimit", history.ScaleLimit2D);
                });
            }

            renderGraph.ResourceMap.SetRenderPassData(new Result(current), renderGraph.FrameIndex);

            var newBentNormalOcclusion = renderGraph.GetTexture(width, height, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Resolve"))
            {
                pass.Initialize(material, 3);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(newBentNormalOcclusion, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", current);
                pass.ReadTexture("_BentNormalOcclusion", bentNormalOcclusion);
                pass.AddRenderPassData<TemporalAA.TemporalAAData>();

                var data = pass.SetRenderFunction<TemporalPassData>((command, pass, data) =>
                {
                    pass.SetFloat(command, "_AoStrength", settings.Strength);
                    pass.SetVector(command, "InputScaleLimit", current.ScaleLimit2D);
                });
            }

            bentNormalOcclusion = newBentNormalOcclusion;
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

        private class Pass1Data
        {
            internal float radius;
            internal float aoStrength;
            internal float falloffScale;
            internal float falloffBias;
            internal int sampleCount;
            internal float rawRadius;
            internal float thinOccluderCompensation;
        }

        private class Pass2Data
        {
            internal VolumetricLighting.Result volumetricLightingResult;
            internal Vector4 scaledResolution;
        }

        private class TemporalPassData
        {
        }
    }
}