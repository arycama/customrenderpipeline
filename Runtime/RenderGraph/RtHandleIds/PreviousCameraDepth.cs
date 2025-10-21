using UnityEngine;

public readonly struct PreviousCameraDepth : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(PreviousCameraDepth));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(PreviousCameraDepth)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}