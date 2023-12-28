using System;
using UnityEngine;
using UnityEngine.Rendering;

public class DepthOfField
{
    [Serializable]
    public class Settings
    {
        [SerializeField, Min(0f)] private float sampleRadius = 8f;
        [SerializeField, Range(1, 128)] private int sampleCount = 8;

        public float SampleRadius => sampleRadius;
        public int SampleCount => sampleCount;
    }

    private Settings settings;
    private LensSettings lensSettings;

    public DepthOfField(Settings settings, LensSettings lensSettings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.lensSettings = lensSettings ?? throw new ArgumentNullException(nameof(lensSettings));
    }

    public RenderTargetIdentifier Render(CommandBuffer command, int width, int height, float fieldOfView, RenderTargetIdentifier color, RenderTargetIdentifier depth)
    {
        var computeShader = Resources.Load<ComputeShader>("PostProcessing/DepthOfField");

        var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.RGB111110Float) { enableRandomWrite = true };
        var tempId = Shader.PropertyToID("_DepthOfFieldResult");
        command.GetTemporaryRT(tempId, desc);

        float sensorSize = lensSettings.SensorHeight / 1000f; // Divide by 1000 to convert from mm to m
        var focalLength = 0.5f * sensorSize / Mathf.Tan(fieldOfView * Mathf.Deg2Rad / 2.0f);

        float F = focalLength;
        float A = focalLength / lensSettings.Aperture;
        float P = lensSettings.FocalDistance;
        float maxCoC = (A * F) / Mathf.Max((P - F), 1e-6f);

        command.SetComputeFloatParam(computeShader, "_FocalDistance", lensSettings.FocalDistance);
        command.SetComputeFloatParam(computeShader, "_FocalLength", focalLength);
        command.SetComputeFloatParam(computeShader, "_ApertureSize", lensSettings.Aperture);
        command.SetComputeFloatParam(computeShader, "_MaxCoC", maxCoC);
        command.SetComputeFloatParam(computeShader, "_SensorHeight", lensSettings.SensorHeight / 1000f);

        command.SetComputeFloatParam(computeShader, "_SampleRadius", settings.SampleRadius);
        command.SetComputeIntParam(computeShader, "_SampleCount", settings.SampleCount);

        command.SetComputeTextureParam(computeShader, 0, "_Input", color);
        command.SetComputeTextureParam(computeShader, 0, "_Depth", depth);
        command.SetComputeTextureParam(computeShader, 0, "_Result", tempId);

        command.DispatchNormalized(computeShader, 0, width, height, 1);
        return tempId;
    }
}