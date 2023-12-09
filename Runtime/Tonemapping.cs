using System;
using UnityEngine;
using UnityEngine.Rendering;

public class Tonemapping
{
    private readonly Settings settings;
    private readonly Bloom.Settings bloomSettings;
    private readonly Material tonemappingMaterial;

    [Serializable]
    public class Settings
    {
        [SerializeField] private float toeStrength = 0.5f;
        [SerializeField] private float toeLength = 0.5f;
        [SerializeField] private float shoulderStrength = 2.0f;
        [SerializeField] private float shoulderLength = 0.5f;
        [SerializeField] private float shoulderAngle = 1.0f;

        public float ToeStrength => toeStrength;
        public float ToeLength => toeLength;
        public float ShoulderStrength => shoulderStrength;
        public float ShoulderLength => shoulderLength;
        public float ShoulderAngle => shoulderAngle;
    }

    public Tonemapping(Settings settings, Bloom.Settings bloomSettings)
    {
        this.settings = settings;
        this.bloomSettings = bloomSettings;
        tonemappingMaterial = new Material(Shader.Find("Hidden/Tonemapping")) { hideFlags = HideFlags.HideAndDontSave };
    }

    public void Render(CommandBuffer command, RenderTargetIdentifier input, RenderTargetIdentifier bloom, bool isSceneView)
    {
        using var profilerScope = command.BeginScopedSample("Tonemapping");

        command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        command.SetGlobalTexture("_MainTex", input);
        command.SetGlobalTexture("_Bloom", bloom);
        command.SetGlobalFloat("_BloomStrength", bloomSettings.Strength);
        command.SetGlobalFloat("_IsSceneView", isSceneView ? 1f : 0f);

        command.SetGlobalFloat("ToeStrength", settings.ToeStrength);
        command.SetGlobalFloat("ToeLength", settings.ToeLength);
        command.SetGlobalFloat("ShoulderStrength", settings.ShoulderStrength);
        command.SetGlobalFloat("ShoulderLength", settings.ShoulderLength);
        command.SetGlobalFloat("ShoulderAngle", settings.ShoulderAngle);

        command.DrawProcedural(Matrix4x4.identity, tonemappingMaterial, 0, MeshTopology.Triangles, 3);
    }
}