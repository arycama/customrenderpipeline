using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class ParticleSurfaceShaderGui : ShaderGUI
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
				material.SetFloat("SrcBlend", (float)BlendMode.One);
				material.SetFloat("DstBlend", (float)BlendMode.Zero);
				material.renderQueue = (int)RenderQueue.Geometry;
				break;
			case Mode.Cutout:
				material.SetFloat("SrcBlend", (float)BlendMode.One);
				material.SetFloat("DstBlend", (float)BlendMode.Zero);
				material.renderQueue = (int)RenderQueue.AlphaTest;
				break;
			case Mode.Fade:
				material.SetFloat("SrcBlend", (float)BlendMode.SrcAlpha);
				material.SetFloat("DstBlend", (float)BlendMode.OneMinusSrcAlpha);
				material.renderQueue = (int)RenderQueue.Transparent;
				break;
			case Mode.Transparent:
				material.SetFloat("SrcBlend", (float)BlendMode.One);
				material.SetFloat("DstBlend", (float)BlendMode.OneMinusSrcAlpha);
				material.renderQueue = (int)RenderQueue.Transparent;
				break;
		}

		//material.ToggleKeyword("BENT_NORMAL", material.GetTexture("BentNormal") != null);
		//material.ToggleKeyword("PARALLAX", material.GetTexture("Height") != null && material.GetFloat("HeightScale") > 0);
		material.ToggleKeyword("EMISSIVE", material.GetTexture("Emissive") != null && material.GetColor("EmissiveColor") != Color.black);
	}
}
