﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Arycama.CustomRenderPipeline
{
    public enum ExposureMode
    {
        Automatic,
        Manual
    }

    public enum MeteringMode
    {
        Uniform,
        Spot,
        Center,
        Mask,
        Procedural
    }

    public class AutoExposure : RenderFeature<(RTHandle input, int width, int height)>
    {
        [Serializable]
        public class Settings
        {
            [field: SerializeField] public ExposureMode ExposureMode { get; private set; } = ExposureMode.Automatic;
            [field: SerializeField] public float AdaptationSpeed { get; private set; } = 1.1f;
            [field: SerializeField, Range(0.0f, 100.0f)] public float HistogramMin { get; private set; } = 40.0f;
            [field: SerializeField, Range(0.0f, 100.0f)] public float HistogramMax { get; private set; } = 90.0f;
            [field: SerializeField] public float MinEv { get; private set; } = -10f;
            [field: SerializeField] public float MaxEv { get; private set; } = 18f;

            [field: Header("Metering")]
            [field: SerializeField] public MeteringMode MeteringMode { get; private set; } = MeteringMode.Center;
            [field: SerializeField] public Vector2 ProceduralCenter { get; private set; } = new(0.5f, 0.5f);
            [field: SerializeField] public Vector2 ProceduralRadii { get; private set; } = new(0.2f, 0.3f);
            [field: SerializeField, Min(0.0f)] public float ProceduralSoftness { get; private set; } = 0.5f;


            [field: Header("Exposure Compensation")]
            [field: SerializeField] public float ExposureCompensation { get; private set; } = 0.0f;
            [field: SerializeField] public int ExposureResolution { get; private set; } = 128;
            [field: SerializeField] public AnimationCurve ExposureCurve { get; private set; } = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 1.0f);
        }

        private readonly Settings settings;
        private readonly LensSettings lensSettings;
        private readonly ComputeShader computeShader;
        private readonly Dictionary<Camera, GraphicsBuffer> exposureBuffers = new();

        private readonly Texture2D exposureTexture;

        public AutoExposure(Settings settings, LensSettings lensSettings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.settings = settings;
            this.lensSettings = lensSettings;
            computeShader = Resources.Load<ComputeShader>("PostProcessing/AutoExposure");


            var exposurePixels = ArrayPool<float>.Get(settings.ExposureResolution);
            for (var i = 0; i < settings.ExposureResolution; i++)
            {
                var uv = i / (settings.ExposureResolution - 1f);
                var t = Mathf.Lerp(settings.MinEv, settings.MaxEv, uv);
                var exposure = settings.ExposureCurve.Evaluate(t);
                exposurePixels[i] = exposure;
            }

            exposureTexture = new Texture2D(settings.ExposureResolution, 1, TextureFormat.RFloat, false) { hideFlags = HideFlags.HideAndDontSave };
            exposureTexture.SetPixelData(exposurePixels, 0);
            ArrayPool<float>.Release(exposurePixels);
            exposureTexture.Apply(false, false);
        }

        protected override void Cleanup(bool disposing)
        {
            foreach(var buffer in exposureBuffers)
            {
                if (buffer.Value != null)
                {
                    buffer.Value.Dispose();
                }
            }

            Object.DestroyImmediate(exposureTexture);
        }

        public void OnPreRender(Camera camera)
        {
            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Auto Exposure"))
            {
                var isFirst = !exposureBuffers.TryGetValue(camera, out var exposureBuffer);
                if (isFirst)
                {
                    exposureBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, 4, sizeof(float)) { name = "Auto Exposure Buffer" };
                    exposureBuffers.Add(camera, exposureBuffer);
                }

                var bufferHandle = renderGraph.ImportBuffer(exposureBuffer);

                // For first pass, set to 1.0f 
                if (isFirst)
                {
                    pass.SetRenderFunction(bufferHandle, (command, pass, data) =>
                    {
                        var initialData = ArrayPool<Vector4>.Get(1);
                        initialData[0] = new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
                        command.SetBufferData(data, initialData);
                        ArrayPool<Vector4>.Release(initialData);
                    });
                }

                renderGraph.SetResource(new AutoExposureData(bufferHandle, isFirst));;
            }
        }

        public override void Render((RTHandle input, int width, int height) data)
        {
            var exposurePixels = exposureTexture.GetRawTextureData<float>();

            for (var i = 0; i < settings.ExposureResolution; i++)
            {
                var uv = i / (settings.ExposureResolution - 1f);
                var t = Mathf.Lerp(settings.MinEv, settings.MaxEv, uv);
                var exposure = settings.ExposureCurve.Evaluate(t);
                exposurePixels[i] = exposure;
            }
            exposureTexture.SetPixelData(exposurePixels, 0);
            exposureTexture.Apply(false, false);

            var histogram = renderGraph.GetBuffer(256);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Auto Exposure"))
            {
                pass.Initialize(computeShader, 0, data.width, data.height);
                pass.ReadTexture("Input", data.input);
                pass.WriteBuffer("LuminanceHistogram", histogram);
                pass.AddRenderPassData<AutoExposureData>();

                pass.SetRenderFunction(
                (
                    minEv: settings.MinEv,
                    maxEv: settings.MaxEv,
                    adaptationSpeed: settings.AdaptationSpeed,
                    exposureCompensation: settings.ExposureCompensation,
                    iso: lensSettings.Iso,
                    aperture: lensSettings.Aperture,
                    shutterSpeed: lensSettings.ShutterSpeed,
                    histogramMin: settings.HistogramMin,
                    histogramMax: settings.HistogramMax,
                    exposureCompensationRemap: GraphicsUtilities.HalfTexelRemap(settings.ExposureResolution, 1)
                ),

                (command, pass, data) => 
                {
                    pass.SetFloat("MinEv", data.minEv);
                    pass.SetFloat("MaxEv", data.maxEv);
                    pass.SetFloat("AdaptationSpeed", data.adaptationSpeed);
                    pass.SetFloat("ExposureCompensation", data.exposureCompensation);
                    pass.SetFloat("Iso", data.iso);
                    pass.SetFloat("Aperture", data.aperture);
                    pass.SetFloat("ShutterSpeed", data.shutterSpeed);
                    pass.SetFloat("HistogramMin", data.histogramMin);
                    pass.SetFloat("HistogramMax", data.histogramMax);
                    pass.SetFloat("MeteringMode", (float)settings.MeteringMode);
                    pass.SetVector("_ExposureCompensationRemap", data.exposureCompensationRemap);
                    pass.SetVector("ProceduralCenter", settings.ProceduralCenter);
                    pass.SetVector("ProceduralRadii", settings.ProceduralRadii);
                    pass.SetFloat("ProceduralSoftness", settings.ProceduralSoftness);
                });
            }

            var output = renderGraph.GetBuffer(1, 16, target: GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Auto Exposure"))
            {
                pass.Initialize(computeShader, 1, 1);
                pass.ReadBuffer("LuminanceHistogram", histogram);
                pass.WriteBuffer("LuminanceOutput", output);
                pass.AddRenderPassData<AutoExposureData>();

                pass.SetRenderFunction((settings.ExposureMode, exposureTexture), (command, pass, data) =>
                {
                    pass.SetFloat("Mode", (float)data.ExposureMode);
                    pass.SetFloat("IsFirst", 0.0f);
                    pass.SetTexture("ExposureTexture", data.exposureTexture);
                });
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Auto Exposure"))
            {
                pass.SetRenderFunction(output, (command, pass, data) =>
                {
                    var exposureData = pass.RenderGraph.GetResource<AutoExposureData>();
                    command.CopyBuffer(data, exposureData.exposureBuffer);
                });
            }
        }

        public readonly struct AutoExposureData : IRenderPassData
        {
            public BufferHandle exposureBuffer { get; }
            public bool IsFirst { get; }

            public AutoExposureData(BufferHandle exposureBuffer, bool isFirst)
            {
                this.exposureBuffer = exposureBuffer ?? throw new ArgumentNullException(nameof(exposureBuffer));
                IsFirst = isFirst;
            }

            public readonly void SetInputs(RenderPass pass)
            {
                pass.ReadBuffer("Exposure", exposureBuffer);
            }

            public readonly void SetProperties(RenderPass pass, CommandBuffer command)
            {
            }
        }
    }
}