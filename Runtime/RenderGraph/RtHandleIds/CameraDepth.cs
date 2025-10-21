using UnityEngine;

public readonly struct CameraDepth : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(CameraDepth));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(CameraDepth)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}