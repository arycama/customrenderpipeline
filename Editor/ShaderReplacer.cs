using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ShaderReplacer : ScriptableWizard
{
	[SerializeField] private Shader source;
	[SerializeField] private Shader destination;

	[MenuItem("Tools/Shader Replacer")]
	public static void OnMenuSelect()
	{
		DisplayWizard<ShaderReplacer>("Shader Replacer", "Replace and Close", "Replace");
	}

	private void OnWizardCreate()
	{
		Replace();
	}

	private void OnWizardOtherButton()
	{
		Replace();
	}

	private void Replace()
	{
		var guids = AssetDatabase.FindAssets("t:Material");
		foreach(var guid in guids)
		{
			var path = AssetDatabase.GUIDToAssetPath(guid);
			var material = AssetDatabase.LoadAssetAtPath<Material>(path);

			if (material.shader == source)
			{
				material.shader = destination;
				EditorUtility.SetDirty(material);
			}
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}
}
