using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class LitSurfaceShaderGUI : ShaderGUI
{
    public enum Mode
    {
        Opaque,
        Cutout,
        Fade,
        Transparent
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties);

        var material = materialEditor.target as Material;

        var mode = (Mode)FindProperty("Mode", properties).floatValue;
        switch (mode)
        {
            case Mode.Opaque:
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.renderQueue = (int)RenderQueue.Geometry;
                break;
            case Mode.Cutout:
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.renderQueue = (int)RenderQueue.AlphaTest;
                break;
            case Mode.Fade:
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.renderQueue = (int)RenderQueue.Transparent;
                break;
            case Mode.Transparent:
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.renderQueue = (int)RenderQueue.Transparent;
                break;
        }
    }
}