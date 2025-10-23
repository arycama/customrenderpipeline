using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public class SpeedTreeShaderGui : ShaderGUI
{
	public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
	{
		base.OnGUI(materialEditor, properties);

		var material = materialEditor.target as Material;

		// Assign _Subsurface property depending on whether subsurface texture is null or not
		var subsurfaceTexProperty = FindProperty("Subsurface", properties);
		var isSubsurface = subsurfaceTexProperty.textureValue != null;
		material.ToggleKeyword("SUBSURFACE_ON", isSubsurface);
		material.SetInteger("StencilRef", isSubsurface ? 17 : 1);
		material.SetInteger("StencilRefMotion", isSubsurface ? 19 : 3);

		var albedoOpacityProperty = FindProperty("AlbedoOpacity", properties);
		var albedoOpacity = albedoOpacityProperty.textureValue;
		var isCutout = albedoOpacity != null && GraphicsFormatUtility.HasAlphaChannel(albedoOpacity.graphicsFormat);
		material.ToggleKeyword("CUTOUT_ON", isCutout);
		material.SetFloat("DoubleSided", isCutout ? 0 : 2);

		var heightProperty = FindProperty("Height", properties);
		material.ToggleKeyword("PARALLAX_ON", heightProperty.textureValue != null && material.GetFloat("HeightScale") > 0);
		material.renderQueue = (int)(isCutout ? RenderQueue.AlphaTest : RenderQueue.Geometry);

		EditorUtility.SetDirty(material);
	}
}
