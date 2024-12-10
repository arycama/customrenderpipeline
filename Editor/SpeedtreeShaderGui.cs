// Created by Ben Sims 22/10/21

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SpeedTreeShaderGui : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        base.OnGUI(materialEditor, properties);

        // Assign _Subsurface property depending on whether subsurface texture is null or not
        var subsurfaceTexProperty = FindProperty("_SubsurfaceTex", properties);
        var subsurfaceProperty = FindProperty("_Subsurface", properties);
        subsurfaceProperty.floatValue = subsurfaceTexProperty.textureValue == null ? 0f : 1f;

        var cutoutProperty = FindProperty("_Cutout", properties);
        var cullProperty = FindProperty("_Cull", properties);

        var material = materialEditor.target as Material;
        if (cutoutProperty.floatValue > 0f)
        {
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        else
        {
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
    }
}