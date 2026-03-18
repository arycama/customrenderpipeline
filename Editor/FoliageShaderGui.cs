using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class FoliageShaderGui : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties);

        var material = materialEditor.target as Material;

        var opacityProperty = FindProperty("Opacity", properties);
        var opacity = opacityProperty.textureValue;
        var isCutout = opacity != null;
        material.ToggleKeyword("CUTOUT_ON", isCutout);
        material.SetFloat("DoubleSided", isCutout ? 0 : 2);
        material.SetInteger("StencilRef", isCutout ? 17 : 1);
        material.SetInteger("StencilRefMotion", isCutout ? 19 : 3);
        material.renderQueue = (int)(isCutout ? RenderQueue.AlphaTest : RenderQueue.Geometry);

        EditorUtility.SetDirty(material);
    }
}