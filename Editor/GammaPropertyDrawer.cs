using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(GammaAttribute))]
public class GammaDrawer : PropertyDrawer
{
	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
	{
		var gamma = (GammaAttribute)attribute;

		var linearValue = property.propertyType == SerializedPropertyType.Float ? property.floatValue : property.intValue;

		var gammaValue = Mathf.Pow(linearValue, 1.0f / gamma.Gamma);

		// Draw the slider or float field in gamma space
		EditorGUI.BeginChangeCheck();

		var fieldRect = EditorGUI.PrefixLabel(position, label);
		gammaValue = EditorGUI.FloatField(fieldRect, gammaValue);

		// Clamp to avoid negative values
		gammaValue = Mathf.Max(0, gammaValue);

		if (EditorGUI.EndChangeCheck())
		{
			// Convert back to linear space
			linearValue = Mathf.Pow(gammaValue, gamma.Gamma);

			// Apply the value
			if (property.propertyType == SerializedPropertyType.Float)
			{
				property.floatValue = linearValue;
			}
			else
			{
				property.intValue = Mathf.RoundToInt(linearValue);
			}
		}
	}
}