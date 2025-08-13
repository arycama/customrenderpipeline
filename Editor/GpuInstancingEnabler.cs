using UnityEditor;
using UnityEngine;

public class GpuInstancingEnabler 
{
	[MenuItem("Tools/Enable GPU Instancing on Materials")]
	public static void OnMenuSelect()
	{
		var guids = AssetDatabase.FindAssets("t:Material");
		foreach (var guid in guids)
		{
			var path = AssetDatabase.GUIDToAssetPath(guid);
			var material = AssetDatabase.LoadAssetAtPath<Material>(path);

			if (material.enableInstancing)
				continue;

			material.enableInstancing = true;
			EditorUtility.SetDirty(material);
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}
}
