using UnityEditor;
using UnityEngine;

public class ShaderFinder : ScriptableWizard
{
    [SerializeField] private Shader source;

    [MenuItem("Tools/Find Materials with Shader")]
    public static void OnMenuSelect()
    {
        DisplayWizard<ShaderFinder>("Find Materials with Shader", "Find and Close", "Find");
    }

    private void OnWizardCreate()
    {
        Find();
    }

    private void OnWizardOtherButton()
    {
        Find();
    }

    private void Find()
    {
        var guids = AssetDatabase.FindAssets("t:Material");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material.shader == source)
            {
				Debug.Log(path);
            }
        }
    }
}
