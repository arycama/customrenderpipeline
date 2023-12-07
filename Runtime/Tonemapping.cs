using UnityEngine;
using UnityEngine.Rendering;

public class Tonemapping
{
    private readonly Bloom.Settings bloomSettings;
    private readonly Material tonemappingMaterial;

    public Tonemapping(Bloom.Settings bloomSettings)
    {
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
        command.DrawProcedural(Matrix4x4.identity, tonemappingMaterial, 0, MeshTopology.Triangles, 3);
    }
}