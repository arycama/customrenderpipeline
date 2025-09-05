using UnityEditor;
using UnityEngine;

public class TerrainShaderGui : ShaderGUI, ITerrainLayerCustomUI
{
	bool ITerrainLayerCustomUI.OnTerrainLayerGUI(TerrainLayer terrainLayer, Terrain terrain)
	{
		//EditorGUI.BeginChangeCheck();

		Undo.RecordObject(terrainLayer, "Modify Terrain Layer");

		terrainLayer.tileSize = Vector2.one * Math.Max(0, EditorGUILayout.FloatField("Size", terrainLayer.tileSize.x));
		terrainLayer.smoothness = Math.Max(0, EditorGUILayout.Slider("Opacity", terrainLayer.smoothness, 0, 1));
		terrainLayer.metallic = Math.Max(0, EditorGUILayout.FloatField("Height Scale", terrainLayer.metallic));
		terrainLayer.normalScale = EditorGUILayout.Slider("Stochastic", terrainLayer.normalScale, 0, 1);

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
