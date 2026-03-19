using UnityEditor;
using UnityEngine;

public class Exp2Drawer : MaterialPropertyDrawer
{
    public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
    {
        // Convert the current shader value back to exponent for display
        var currentExponent = prop.floatValue != 0 ? Math.Log2(prop.floatValue) : 0;

        EditorGUI.BeginChangeCheck();

        var newExponent = EditorGUI.FloatField(position, label, currentExponent);

        if (EditorGUI.EndChangeCheck())
        {
            prop.floatValue = Math.Exp2(newExponent);
        }
    }
}