using UnityEditor;
using UnityEngine.Rendering;

public class RenderEditorWindow : EditorWindow
{
    private Editor editor;

    [MenuItem("Window/Rendering/Render Pipeline")]
    public static void OpenWindow()
    {
        _ = GetWindow<RenderEditorWindow>("Render Pipeline");
    }

    private void OnGUI()
    {
        var currentPipeline = GraphicsSettings.currentRenderPipeline;
        if (editor == null || editor.target != currentPipeline)
        {
            if (currentPipeline != null)
                editor = Editor.CreateEditor(GraphicsSettings.currentRenderPipeline);
            else
                editor = null;
        }

        if (editor != null)
            editor.DrawDefaultInspector();
    }
}