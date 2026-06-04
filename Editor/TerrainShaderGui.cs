using UnityEditor;
using UnityEngine;
using Unmath;

public class TerrainShaderGui : ShaderGUI, ITerrainLayerCustomUI
{
	bool ITerrainLayerCustomUI.OnTerrainLayerGUI(TerrainLayer terrainLayer, Terrain terrain)
	{
		//EditorGUI.BeginChangeCheck();

		Undo.RecordObject(terrainLayer, "Modify Terrain Layer");

        terrainLayer.tileSize = Vector2.one * Math.Max(0, EditorGUILayout.Slider("Size (m)", terrainLayer.tileSize.x, 0, 4));
		terrainLayer.smoothness = Math.Max(0, EditorGUILayout.Slider("Opacity", terrainLayer.smoothness, 0, 1));
		terrainLayer.metallic = Math.Max(0, EditorGUILayout.Slider("Height Scale (cm)", terrainLayer.metallic * 32, 0, 32) / 32);
		terrainLayer.normalScale = EditorGUILayout.Slider("Stochastic", terrainLayer.normalScale, 0, 1);

        var spec = terrainLayer.specular;
        spec.r = EditorGUILayout.Toggle("Is Grass", spec.r > 0) ? 1 : 0;
        spec.a = EditorGUILayout.Slider("Translucency", spec.a, 0, 1);
        terrainLayer.specular = spec;

        terrainLayer.diffuseTexture = EditorGUILayout.ObjectField("Albedo", terrainLayer.diffuseTexture, typeof(Texture2D), false) as Texture2D;
		terrainLayer.normalMapTexture = EditorGUILayout.ObjectField("Normal Occlusion Roughness", terrainLayer.normalMapTexture, typeof(Texture2D), false) as Texture2D;
		terrainLayer.maskMapTexture = EditorGUILayout.ObjectField("Height", terrainLayer.maskMapTexture, typeof(Texture2D), false) as Texture2D;

		//if(EditorGUI.EndChangeCheck())
		//{
		//	Undo.RecordObject(terrainLayer, "Modify Terrain Layer");
		//}

		return true;
	}
}
