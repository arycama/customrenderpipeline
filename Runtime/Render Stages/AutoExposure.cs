using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class AutoExposure
    {
        [Serializable]
        public class Settings
        {
            [SerializeField] private float minEv = -10f;
            [SerializeField] private float maxEv = 18f;
            [SerializeField] private float adaptationSpeed = 1.1f;
            [SerializeField] private float exposureCompensation = 0.0f;
            [SerializeField] private Vector2 histogramPercentages = new(40f, 90f);
            [SerializeField] private int exposureResolution = 128;
            [SerializeField] private AnimationCurve exposureCurve = AnimationCurve.Linear(-10f, -10f, 20f, 20f);

            public float MinEv => minEv;
            public float MaxEv => maxEv;
            public float AdaptationSpeed => adaptationSpeed;
            public float ExposureCompensation => exposureCompensation;
            public AnimationCurve ExposureCurve => exposureCurve;
            public Vector2 HistogramPercentages => histogramPercentages;
            public int ExposureResolution => exposureResolution;
        }

        private readonly Settings settings;
        private readonly LensSettings lensSettings;
        private readonly ComputeShader computeShader;
        private readonly GraphicsBuffer histogram, output, exposureBuffer;

        private Texture2D exposureTexture;
        private float[] exposurePixels;

        public AutoExposure(Settings settings, LensSettings lensSettings)
        {
            this.settings = settings;
            this.lensSettings = lensSettings;
            computeShader = Resources.Load<ComputeShader>("PostProcessing/AutoExposure");

            histogram = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, sizeof(uint));
            output = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, 4, sizeof(float));
            exposureBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, 4, sizeof(float));

            exposurePixels = new float[settings.ExposureResolution];
            for (var i = 0; i < settings.ExposureResolution; i++)
            {
                var uv = i / (settings.ExposureResolution - 1f);
                var t = Mathf.Lerp(settings.MinEv, settings.MaxEv, uv);
                var exposure = settings.ExposureCurve.Evaluate(t);
                exposurePixels[i] = exposure;
            }

            exposureTexture = new Texture2D(settings.ExposureResolution, 1, TextureFormat.RFloat, false) { hideFlags = HideFlags.HideAndDontSave };
            exposureTexture.SetPixelData(exposurePixels, 0);
            exposureTexture.Apply(false, false);
        }

        public void Render(CommandBuffer command, RenderTargetIdentifier input, int width, int height)
        {
            exposurePixels = new float[settings.ExposureResolution];
            for (var i = 0; i < settings.ExposureResolution; i++)
            {
                var uv = i / (settings.ExposureResolution - 1f);
                var t = Mathf.Lerp(settings.MinEv, settings.MaxEv, uv);
                var exposure = settings.ExposureCurve.Evaluate(t);
                exposurePixels[i] = exposure;
            }
            exposureTexture.SetPixelData(exposurePixels, 0);
            exposureTexture.Apply(false, false);

            command.SetComputeFloatParam(computeShader, "MinEv", settings.MinEv);
            command.SetComputeFloatParam(computeShader, "MaxEv", settings.MaxEv);
            command.SetComputeFloatParam(computeShader, "AdaptationSpeed", settings.AdaptationSpeed);
            command.SetComputeFloatParam(computeShader, "ExposureCompensation", settings.ExposureCompensation);

            command.SetComputeFloatParam(computeShader, "Iso", lensSettings.Iso);
            command.SetComputeFloatParam(computeShader, "Aperture", lensSettings.Aperture);
            command.SetComputeFloatParam(computeShader, "ShutterSpeed", lensSettings.ShutterSpeed);

            command.SetComputeFloatParam(computeShader, "HistogramMin", settings.HistogramPercentages.x);
            command.SetComputeFloatParam(computeShader, "HistogramMax", settings.HistogramPercentages.y);

            command.SetComputeBufferParam(computeShader, 0, "LuminanceHistogram", histogram);
            command.SetComputeTextureParam(computeShader, 0, "Input", input);
            command.SetComputeVectorParam(computeShader, "_ExposureCompensationRemap", GraphicsUtilities.HalfTexelRemap(settings.ExposureResolution, 1));

            command.DispatchNormalized(computeShader, 0, width, height, 1);

            command.SetComputeBufferParam(computeShader, 1, "LuminanceHistogram", histogram);
            command.SetComputeBufferParam(computeShader, 1, "LuminanceOutput", output);
            command.SetComputeTextureParam(computeShader, 1, "ExposureTexture", exposureTexture);
            command.DispatchCompute(computeShader, 1, 1, 1, 1);

            command.CopyBuffer(output, exposureBuffer);
            command.SetGlobalConstantBuffer(exposureBuffer, "Exposure", 0, sizeof(float) * 4);
        }
    }
}