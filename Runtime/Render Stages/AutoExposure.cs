﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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

    public class AutoExposure : RenderFeature
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


        private struct Pass2Data
        {
            internal BufferHandle output;
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

                renderGraph.ResourceMap.SetRenderPassData(new AutoExposureData(bufferHandle, isFirst), renderGraph.FrameIndex);
            }
        }

        public void Render(RTHandle input, int width, int height)
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
                pass.Initialize(computeShader, 0, width, height);
                pass.ReadTexture("Input", input);
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
                    pass.SetFloat(command, "MinEv", data.minEv);
                    pass.SetFloat(command, "MaxEv", data.maxEv);
                    pass.SetFloat(command, "AdaptationSpeed", data.adaptationSpeed);
                    pass.SetFloat(command, "ExposureCompensation", data.exposureCompensation);
                    pass.SetFloat(command, "Iso", data.iso);
                    pass.SetFloat(command, "Aperture", data.aperture);
                    pass.SetFloat(command, "ShutterSpeed", data.shutterSpeed);
                    pass.SetFloat(command, "HistogramMin", data.histogramMin);
                    pass.SetFloat(command, "HistogramMax", data.histogramMax);
                    pass.SetFloat(command, "MeteringMode", (float)settings.MeteringMode);
                    pass.SetVector(command, "_ExposureCompensationRemap", data.exposureCompensationRemap);
                    pass.SetVector(command, "ProceduralCenter", settings.ProceduralCenter);
                    pass.SetVector(command, "ProceduralRadii", settings.ProceduralRadii);
                    pass.SetFloat(command, "ProceduralSoftness", settings.ProceduralSoftness);
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
                    pass.SetFloat(command, "Mode", (float)data.ExposureMode);
                    pass.SetFloat(command, "IsFirst", 0.0f);
                    pass.SetTexture(command, "ExposureTexture", data.exposureTexture);
                });
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Auto Exposure"))
            {
                pass.SetRenderFunction(output, (command, pass, data) =>
                {
                    var exposureData = pass.RenderGraph.ResourceMap.GetRenderPassData<AutoExposureData>(renderGraph.FrameIndex);
                    command.CopyBuffer(data, exposureData.exposureBuffer);
                });
            }
        }

        private struct PassData
        {
            internal bool isFirst;
            internal BufferHandle bufferHandle;
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