using UnityEditor;
using UnityEngine;

public class Ev100Drawer : MaterialPropertyDrawer
{
    public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
    {
        EditorGUI.BeginChangeCheck();

        var currentExponent = prop.floatValue != 0 ? PhysicalCameraUtility.LuminanceToEV100(prop.floatValue) : 0;
        var newExponent = EditorGUI.FloatField(position, label, currentExponent);

        if (EditorGUI.EndChangeCheck())
            prop.floatValue = PhysicalCameraUtility.EV100ToLuminance(newExponent);
    }
}