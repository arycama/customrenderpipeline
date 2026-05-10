using System;
using UnityEditor;
using UnityEngine;

public class TerrainLayerUI : ShaderGUI, ITerrainLayerCustomUI
{
    public static event Action<TerrainLayer> OnTerrainLayerChanged;

    bool ITerrainLayerCustomUI.OnTerrainLayerGUI(TerrainLayer terrainLayer, Terrain terrain)
    {
        Undo.RecordObject(terrainLayer, "Modify Terrain Layer");

        EditorGUIUtility.labelWidth = 0;

        EditorGUILayout.LabelField("Textures", EditorStyles.boldLabel);
        using (var changed = new EditorGUI.ChangeCheckScope())
        {
            terrainLayer.diffuseTexture = EditorGUILayout.ObjectField("Albedo, Smoothness", terrainLayer.diffuseTexture, typeof(Texture2D), false) as Texture2D;
            TerrainLayerUtility.ValidateDiffuseTextureUI(terrainLayer.diffuseTexture);

            terrainLayer.normalMapTexture = EditorGUILayout.ObjectField("Normal Map", terrainLayer.normalMapTexture, typeof(Texture2D), false) as Texture2D;
            TerrainLayerUtility.ValidateNormalMapTextureUI(terrainLayer.normalMapTexture, TerrainLayerUtility.CheckNormalMapTextureType(terrainLayer.normalMapTexture));

            terrainLayer.maskMapTexture = EditorGUILayout.ObjectField("Metal, Occlusion, Height", terrainLayer.maskMapTexture, typeof(Texture2D), false) as Texture2D;
            TerrainLayerUtility.ValidateMaskMapTextureUI(terrainLayer.maskMapTexture);

            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);

            terrainLayer.normalScale = Mathf.Max(0, EditorGUILayout.FloatField("Stochastic Scale", terrainLayer.normalScale));
            terrainLayer.smoothness = EditorGUILayout.Slider("Blend Smoothness", terrainLayer.smoothness, 1e-3f, 1);

            // Metallic, diffuse remap min/max, and specColor are unused. They could be used in the future if we need more custom data
            terrainLayer.metallic = EditorGUILayout.Slider("Metallic", terrainLayer.metallic, 0, 1);

            var maskRemapMin = terrainLayer.maskMapRemapMin;
            var maskRemapMax = terrainLayer.maskMapRemapMax;

            EditorGUILayout.MinMaxSlider("Metallic Remap:", ref maskRemapMin.x, ref maskRemapMax.x, 0, 1);
            EditorGUILayout.MinMaxSlider("AO Remap:", ref maskRemapMin.y, ref maskRemapMax.y, 0, 1);

            EditorGUILayout.LabelField("Height Remap:");
            EditorGUILayout.BeginHorizontal();
            //var heightRemap = EditorGUILayout.Vector2Field(string.Empty, new Vector2(maskRemapMin.z, maskRemapMax.z));
            //maskRemapMin.z = heightRemap.x;
            //maskRemapMax.z = heightRemap.y;

            maskRemapMin.z = EditorGUILayout.FloatField(maskRemapMin.z);
            EditorGUILayout.MinMaxSlider(ref maskRemapMin.z, ref maskRemapMax.z, -1, 2);
            maskRemapMax.z = EditorGUILayout.FloatField(maskRemapMax.z);
            EditorGUILayout.EndHorizontal();

            terrainLayer.maskMapRemapMin = maskRemapMin;
            terrainLayer.maskMapRemapMax = maskRemapMax;

            EditorGUILayout.Space();
            TerrainLayerUtility.TilingSettingsUI(terrainLayer);

            if (changed.changed)
            {
                OnTerrainLayerChanged?.Invoke(terrainLayer);
            }
        }

        return true;
    }
}