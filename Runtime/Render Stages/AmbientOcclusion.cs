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
        private RayTracingShader ambientOcclusionRaytracingShader;

        private PersistentRTHandleCache temporalCache;

        public AmbientOcclusion(Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            material = new Material(Shader.Find("Hidden/Ambient Occlusion")) { hideFlags = HideFlags.HideAndDontSave };
            temporalCache = new(GraphicsFormat.R16G16B16A16_SFloat, renderGraph, "Physical Sky");
            ambientOcclusionRaytracingShader = Resources.Load<RayTracingShader>("Raytracing/AmbientOcclusion");
        }

        public void Render(Camera camera, RTHandle depth, float scale, Texture2D blueNoise2D, Matrix4x4 invVpMatrix, RTHandle normal, ICommonPassData commonPassData, RTHandle velocity, ref RTHandle bentNormalOcclusion, float bias, float distantBias)
        {
            var width = (int)(camera.pixelWidth * scale);
            var height = (int)(camera.pixelHeight * scale);

            var tempResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);
            var hitResult = renderGraph.GetTexture(width, height, GraphicsFormat.R16G16B16A16_SFloat, isScreenTexture: true);

            if (settings.UseRaytracing)
            {
                using (var pass = renderGraph.AddRenderPass<RaytracingRenderPass>("Raytraced Ambient Occlusion"))
                {
                    var raytracingData = renderGraph.ResourceMap.GetRenderPassData<RaytracingResult>();

                    pass.Initialize(ambientOcclusionRaytracingShader, "RayGeneration", "RaytracingVisibility", raytracingData.Rtas, width, height, 1, bias, distantBias);
                    pass.WriteTexture(tempResult, "HitColor");
                    pass.WriteTexture(hitResult, "HitResult");
                    pass.ReadTexture("_Depth", depth);

                    commonPassData.SetInputs(pass);

                    var data = pass.SetRenderFunction<Pass1Data>((command, pass, data) =>
                    {
                        commonPassData.SetProperties(pass, command);
                        pass.SetVector(command, "ScaleOffset", data.scaleOffset);
                        pass.SetVector(command, "_UvToView", data.uvToView);
                        pass.SetFloat(command, "_Radius", data.radius);
                        pass.SetFloat(command, "_AoStrength", data.aoStrength);
                        pass.SetFloat(command, "_FalloffScale", data.falloffScale);
                        pass.SetFloat(command, "_FalloffBias", data.falloffBias);
                        pass.SetInt(command, "_DirectionCount", data.directionCount);
                        pass.SetInt(command, "_SampleCount", data.sampleCount);
                        pass.SetTexture(command, "_BlueNoise2D", data.blueNoise2d);
                        pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                        pass.SetMatrix(command, "_ClipToWorld", data.invVpMatrix);
                        pass.SetVector(command, "_CameraDepthScaleLimit", depth.ScaleLimit2D);
                    });

                    var tanHalfFovY = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    var tanHalfFovX = tanHalfFovY * camera.aspect;

                    data.scaleOffset = new Vector2(1.0f / width, 1.0f / height);
                    data.uvToView = new Vector4(tanHalfFovX * 2.0f, tanHalfFovY * 2.0f, -tanHalfFovX, -tanHalfFovY);
                    data.radius = settings.Radius * height / tanHalfFovY * 0.5f;
                    data.aoStrength = settings.Strength;
                    data.falloffScale = settings.Falloff == 1.0f ? 0.0f : 1.0f / (settings.Radius * settings.Falloff - settings.Radius);
                    data.falloffBias = settings.Falloff == 1.0f ? 1.0f : 1.0f / (1.0f - settings.Falloff);
                    data.directionCount = settings.DirectionCount;
                    data.sampleCount = settings.SampleCount;
                    data.blueNoise2d = blueNoise2D;
                    data.scaledResolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
                    data.invVpMatrix = invVpMatrix;
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
                    pass.ReadTexture("_Normals", normal);
                    pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                    commonPassData.SetInputs(pass);

                    var data = pass.SetRenderFunction<Pass1Data>((command, pass, data) =>
                    {
                        commonPassData.SetProperties(pass, command);

                        pass.SetVector(command, "ScaleOffset", data.scaleOffset);
                        pass.SetVector(command, "_UvToView", data.uvToView);
                        pass.SetFloat(command, "_Radius", data.radius);
                        pass.SetFloat(command, "_AoStrength", data.aoStrength);
                        pass.SetFloat(command, "_FalloffScale", data.falloffScale);
                        pass.SetFloat(command, "_FalloffBias", data.falloffBias);
                        pass.SetInt(command, "_DirectionCount", data.directionCount);
                        pass.SetInt(command, "_SampleCount", data.sampleCount);
                        pass.SetTexture(command, "_BlueNoise2D", data.blueNoise2d);
                        pass.SetVector(command, "_ScaledResolution", data.scaledResolution);
                        pass.SetMatrix(command, "_ClipToWorld", data.invVpMatrix);
                        pass.SetVector(command, "_CameraDepthScaleLimit", depth.ScaleLimit2D);
                    });

                    var tanHalfFovY = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    var tanHalfFovX = tanHalfFovY * camera.aspect;

                    data.scaleOffset = new Vector2(1.0f / width, 1.0f / height);
                    data.uvToView = new Vector4(tanHalfFovX * 2.0f, tanHalfFovY * 2.0f, -tanHalfFovX, -tanHalfFovY);
                    data.radius = settings.Radius * height / tanHalfFovY * 0.5f;
                    data.aoStrength = settings.Strength;
                    data.falloffScale = settings.Falloff == 1.0f ? 0.0f : 1.0f / (settings.Radius * settings.Falloff - settings.Radius);
                    data.falloffBias = settings.Falloff == 1.0f ? 1.0f : 1.0f / (1.0f - settings.Falloff);
                    data.directionCount = settings.DirectionCount;
                    data.sampleCount = settings.SampleCount;
                    data.blueNoise2d = blueNoise2D;
                    data.scaledResolution = new Vector4(width, height, 1.0f / width, 1.0f / height);
                    data.invVpMatrix = invVpMatrix;
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
                pass.Initialize(material, 1, camera: camera);
                pass.WriteDepth(depth, RenderTargetFlags.ReadOnlyDepthStencil);
                pass.WriteTexture(current, RenderBufferLoadAction.DontCare);

                pass.ReadTexture("_Input", tempResult);
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

            renderGraph.ResourceMap.SetRenderPassData(new Result(current));

            var newBentNormalOcclusion = renderGraph.GetTexture(width, height, GraphicsFormat.R8G8B8A8_UNorm, isScreenTexture: true);
            using (var pass = renderGraph.AddRenderPass<FullscreenRenderPass>("Ambient Occlusion Resolve"))
            {
                pass.Initialize(material, 2);
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
        public struct Result : IRenderPassData
        {
            public RTHandle AmbientOcclusion { get; }

            public Result(RTHandle ambientOcclusion)
            {
                AmbientOcclusion = ambientOcclusion ?? throw new ArgumentNullException(nameof(ambientOcclusion));
            }

            public void SetInputs(RenderPass pass)
            {
                pass.ReadTexture("AmbientOcclusion", AmbientOcclusion);
            }

            public void SetProperties(RenderPass pass, CommandBuffer command)
            {
            }
        }

        [Serializable]
        public class Settings
        {
            [field: SerializeField, Range(0.0f, 8.0f)] public float Strength { get; private set; } = 1.0f;
            [field: SerializeField] public float Radius { get; private set; } = 5.0f;
            [field: SerializeField, Range(0f, 1f)] public float Falloff { get; private set; } = 0.75f;
            [field: SerializeField, Range(1, 8)] public int DirectionCount { get; private set; } = 1;
            [field: SerializeField, Range(1, 32)] public int SampleCount { get; private set; } = 8;

            [field: SerializeField, Range(0, 32)] public int ResolveSamples { get; private set; } = 8;
            [field: SerializeField, Min(0.0f)] public float ResolveSize { get; private set; } = 16.0f;
            [field: SerializeField] public bool UseRaytracing { get; private set; } = false;
        }

        private class Pass0Data
        {
            internal Vector2 scaleOffset;
            internal Vector4 scaledResolution;
            internal Matrix4x4 invVpMatrix;
        }

        private class Pass1Data
        {
            internal Vector2 scaleOffset;
            internal Vector4 uvToView;
            internal float radius;
            internal float aoStrength;
            internal float falloffScale;
            internal float falloffBias;
            internal int directionCount;
            internal int sampleCount;
            internal Texture2D blueNoise2d;
            internal Vector4 scaledResolution;
            internal Matrix4x4 invVpMatrix;
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