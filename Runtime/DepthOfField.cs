using System;
using UnityEngine;
using UnityEngine.Rendering;

public class DepthOfField
{
    public enum Mode
    {
        SinglePass,
        PointSprite,
        Convolution
    }

    [Serializable]
    public class Settings
    {
        [SerializeField] private Mode mode = Mode.SinglePass;
        [SerializeField, Min(0f)] private float sampleRadius = 8f;
        [SerializeField, Range(1, 128)] private int sampleCount = 8;

        public Mode Mode => mode;
        public float SampleRadius => sampleRadius;
        public int SampleCount => sampleCount;
    }

    private Settings settings;
    private LensSettings lensSettings;
    private Material material;
    private MaterialPropertyBlock propertyBlock;

    public DepthOfField(Settings settings, LensSettings lensSettings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.lensSettings = lensSettings ?? throw new ArgumentNullException(nameof(lensSettings));

        material = new Material(Shader.Find("Hidden/DepthOfField")) { hideFlags = HideFlags.HideAndDontSave };
        propertyBlock = new();
    }

    public RenderTargetIdentifier Render(CommandBuffer command, int width, int height, float fieldOfView, RenderTargetIdentifier color, RenderTargetIdentifier depth)
    {
        if(settings.Mode == Mode.SinglePass)
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
        else // if mode == point sprites
        {
            var desc = new RenderTextureDescriptor(width * 2, height, RenderTextureFormat.RGB111110Float);
            var tempId = Shader.PropertyToID("_DepthOfFieldResult");
            command.GetTemporaryRT(tempId, desc);

            var focalLength = lensSettings.SensorHeight / (2.0f * Mathf.Tan(fieldOfView * Mathf.Deg2Rad / 2.0f));

            float F = focalLength / 1000f;
            float A = focalLength / lensSettings.Aperture;
            float P = lensSettings.FocalDistance;
            float maxCoC = (A * F) / Mathf.Max((P - F), 1e-6f);

            propertyBlock.SetFloat("_FocalDistance", lensSettings.FocalDistance);
            propertyBlock.SetFloat("_FocalLength", focalLength);
            propertyBlock.SetFloat("_ApertureSize", lensSettings.Aperture);
            propertyBlock.SetFloat("_MaxCoC", maxCoC);
            propertyBlock.SetFloat("_SensorHeight", lensSettings.SensorHeight);

            propertyBlock.SetFloat("_SampleRadius", settings.SampleRadius);
            propertyBlock.SetInt("_SampleCount", settings.SampleCount);

            command.SetGlobalTexture("_Input", color);
            command.SetGlobalTexture("_Depth", depth);

            command.SetRenderTarget(tempId);
            command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Quads, width * height * 4);

            command.SetRenderTarget(color);
            command.SetGlobalTexture("_MainTex", tempId);
            command.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3);

            return color;
        }
    }
}