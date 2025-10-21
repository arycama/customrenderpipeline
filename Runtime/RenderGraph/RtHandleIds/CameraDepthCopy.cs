using UnityEngine;

public readonly struct CameraDepthCopy : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(CameraDepthCopy));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(CameraDepthCopy)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}