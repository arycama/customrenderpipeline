using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

public class MaterialDescriptionPreprocessor : AssetPostprocessor
{
    public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        if (GraphicsSettings.currentRenderPipeline is not CustomRenderPipelineAssetBase customRenderPipelineAssetBase)
            return;

        material.shader = customRenderPipelineAssetBase.defaultShader;

        TexturePropertyDescription textureProperty;
        if (description.TryGetProperty("DiffuseColor", out textureProperty) && textureProperty.texture != null)
        {
            SetMaterialTextureProperty("AlbedoMetallic", material, textureProperty);
        }

        static void SetMaterialTextureProperty(string propertyName, Material material, TexturePropertyDescription textureProperty)
        {
            material.SetTexture(propertyName, textureProperty.texture);
            material.SetTextureOffset(propertyName, textureProperty.offset);
            material.SetTextureScale(propertyName, textureProperty.scale);
        }
    }
}
