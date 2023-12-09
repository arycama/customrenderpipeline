using System;
using UnityEngine;
using UnityEngine.Rendering;

public class AutoExposure
{
    [Serializable]
    public class Settings
    {
        [SerializeField] private float minEv = -10f;
        [SerializeField] private float maxEv = 18f;
        [SerializeField] private float adaptationSpeed = 1.1f;
        [SerializeField] private float exposureCompensation = 0.0f;

        public float MinEv => minEv;
        public float MaxEv => maxEv;
        public float AdaptationSpeed => adaptationSpeed;
        public float ExposureCompensation => exposureCompensation;
    }

    private readonly Settings settings;
    private readonly LensSettings lensSettings;
    private readonly ComputeShader computeShader;
    private readonly GraphicsBuffer histogram, output, exposureBuffer;

    public AutoExposure(Settings settings, LensSettings lensSettings)
    {
        this.settings = settings;
        this.lensSettings = lensSettings;
        computeShader = Resources.Load<ComputeShader>("PostProcessing/AutoExposure");

        histogram = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 256, sizeof(uint));
        output = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, 4, sizeof(float));
        exposureBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination, 4, sizeof(float));
    }

    public void Render(CommandBuffer command, RenderTargetIdentifier input, int width, int height)
    {
        command.SetComputeFloatParam(computeShader, "MinEv", settings.MinEv);
        command.SetComputeFloatParam(computeShader, "MaxEv", settings.MaxEv);
        command.SetComputeFloatParam(computeShader, "AdaptationSpeed", settings.AdaptationSpeed);
        command.SetComputeFloatParam(computeShader, "ExposureCompensation", settings.ExposureCompensation);

        command.SetComputeFloatParam(computeShader, "Iso", lensSettings.Iso);
        command.SetComputeFloatParam(computeShader, "Aperture", lensSettings.Aperture);
        command.SetComputeFloatParam(computeShader, "ShutterSpeed", lensSettings.ShutterSpeed);

        command.SetComputeBufferParam(computeShader, 0, "LuminanceHistogram", histogram);
        command.SetComputeTextureParam(computeShader, 0, "Input", input);
        command.DispatchNormalized(computeShader, 0, width, height, 1);

        command.SetComputeBufferParam(computeShader, 1, "LuminanceHistogram", histogram);
        command.SetComputeBufferParam(computeShader, 1, "LuminanceOutput", output);
        command.DispatchCompute(computeShader, 1, 1, 1, 1);

        command.CopyBuffer(output, exposureBuffer);
        command.SetGlobalConstantBuffer(exposureBuffer, "Exposure", 0, sizeof(float) * 4);
    }
}
