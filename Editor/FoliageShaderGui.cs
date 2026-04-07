using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class FoliageShaderGui : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties);

        var material = materialEditor.target as Material;

        var hasHeight = FindProperty("Height", properties).textureValue != null;
        material.ToggleKeyword("PARALLAX_ON", hasHeight);

        var hasOpacity = FindProperty("Opacity", properties).textureValue != null;
        material.ToggleKeyword("CUTOUT_ON", hasOpacity);
        material.SetFloat("DoubleSided", hasOpacity ? 0 : 2);
        material.SetInteger("StencilRef", hasOpacity ? 17 : 1);
        material.SetInteger("StencilRefMotion", hasOpacity ? 19 : 3);
        material.renderQueue = hasOpacity ? (int)RenderQueue.AlphaTest : -1;

        EditorUtility.SetDirty(material);
    }
}