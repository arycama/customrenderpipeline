using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class TextureGenerator : ScriptableWizard
{
    [SerializeField] private Material material;
    [SerializeField] private int resolution = 512;

    [MenuItem("Tools/Texture Generator")]
    public static void OnMenuSelect()
    {
        DisplayWizard<TextureGenerator>(nameof(TextureGenerator), "Generate and Close", "Generate");
    }

    private void OnWizardCreate()
    {
        Generate();
    }

    private void OnWizardOtherButton()
    {
        Generate();
    }

    private void Generate()
    {
        var path = EditorPrefs.GetString("TextureGeneratorPath");
        path = EditorUtility.SaveFilePanelInProject("Title", Path.GetFileName(path), "exr", "message", path);

        if (string.IsNullOrEmpty(path))
            return;

        EditorPrefs.SetString("TextureGeneratorPath", path);

        var target = new RenderTexture(resolution, resolution, 0, GraphicsFormat.R32G32B32A32_SFloat);
        var command = new CommandBuffer();

        command.SetRenderTarget(target);
        command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
        command.RequestAsyncReadback(target, readback =>
        {
            target.Release();
            //var pngBytes = ImageConversion.EncodeNativeArrayToPNG(readback.GetData<byte>(), GraphicsFormat.R8G8B8A8_SRGB, (uint)resolution, (uint)resolution);
            var exrBytes = ImageConversion.EncodeNativeArrayToEXR(readback.GetData<byte>(), GraphicsFormat.R32G32B32A32_SFloat, (uint)resolution, (uint)resolution, flags: Texture2D.EXRFlags.OutputAsFloat);

            File.WriteAllBytes(path, exrBytes.ToArray());
            AssetDatabase.Refresh();
        });

        Graphics.ExecuteCommandBuffer(command);
    }
}
