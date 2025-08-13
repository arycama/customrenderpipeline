using System;

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public static class DistanceField
{
    private static bool isInitialized;
    private static MaterialPropertyBlock propertyBlock;
    private static Material material;

    public static void Generate(CommandBuffer command, RenderTexture result, Texture2D texture, float cutoff, Action<AsyncGPUReadbackRequest> callback)
    {
        if (!isInitialized)
        {
            material = new Material(Shader.Find("Hidden/DistanceField")) { hideFlags = HideFlags.HideAndDontSave };
            propertyBlock = new();
            isInitialized = true;
        }

        // Seed pixels
        var src = Shader.PropertyToID("src");
        command.GetTemporaryRT(src, new RenderTextureDescriptor(texture.width, texture.height, GraphicsFormat.R32G32_SFloat, 0));
        command.SetRenderTarget(src);

        propertyBlock.SetFloat("Cutoff", cutoff);
        propertyBlock.SetFloat("InvResolution", (float)(1.0f / texture.width));
        propertyBlock.SetFloat("Resolution", texture.width);
        propertyBlock.SetTexture("Input", texture);
        command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1, propertyBlock);

        // Jump flood, Ping pong between two temporary textures.
        var passes = Mathf.CeilToInt(Mathf.Log(texture.width, 2));
        var minMaxValues = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 4, sizeof(float));

        for (var i = 0; i < passes; i++)
        {
            var offset = Mathf.Pow(2, passes - i - 1);
            var dst = Shader.PropertyToID($"dst{i}");
            command.GetTemporaryRT(dst, new RenderTextureDescriptor(texture.width, texture.height, GraphicsFormat.R32G32_SFloat, 0));
            command.SetRenderTarget(dst);

            if (i == passes - 1)
            {
                command.SetRandomWriteTarget(1, minMaxValues);
                command.EnableShaderKeyword("FINAL_PASS");
                propertyBlock.SetBuffer("MinMaxValuesWrite", minMaxValues);
            }
            else
            {
                command.DisableShaderKeyword("FINAL_PASS");
            }

            command.SetGlobalTexture("JumpFloodInput", src);
            propertyBlock.SetFloat("Offset", offset);
            propertyBlock.SetTexture("Input", texture);
            propertyBlock.SetFloat("InvResolution", (float)(1.0f / texture.width));
            propertyBlock.SetFloat("Resolution", texture.width);
            propertyBlock.SetFloat("Cutoff", cutoff);

            command.DrawProcedural(Matrix4x4.identity, material, 1, MeshTopology.Triangles, 3, 1, propertyBlock);
            command.ClearRandomWriteTargets();
            command.ReleaseTemporaryRT(src);
            src = dst;
        }

        // Final combination pass
        command.SetRenderTarget(result);

        command.SetGlobalTexture("JumpFloodInput", src);
        propertyBlock.SetBuffer("MinMaxValues", minMaxValues);

        propertyBlock.SetTexture("Input", texture);
        propertyBlock.SetFloat("Cutoff", cutoff);
        propertyBlock.SetFloat("InvResolution", (float)(1.0f / texture.width));
        propertyBlock.SetFloat("Resolution", texture.width);

        command.DrawProcedural(Matrix4x4.identity, material, 2, MeshTopology.Triangles, 3, 1, propertyBlock);
        command.GenerateMips(result);

        // Generate mip maps
        var mipCount = (int)Mathf.Log(Mathf.Max(texture.width, texture.height), 2.0f);
        for (var i = 1; i < mipCount; i++)
        {
            command.SetRenderTarget(result, i);
            propertyBlock.SetTexture("Input", texture);
            command.SetGlobalTexture("JumpFloodInput", src);
            propertyBlock.SetBuffer("MinMaxValues", minMaxValues);
            propertyBlock.SetFloat("Cutoff", cutoff);
            command.DrawProcedural(Matrix4x4.identity, material, 3, MeshTopology.Triangles, 3, 1, propertyBlock);
        }

        command.ReleaseTemporaryRT(src);
        command.RequestAsyncReadback(minMaxValues, callback);
    }
}
