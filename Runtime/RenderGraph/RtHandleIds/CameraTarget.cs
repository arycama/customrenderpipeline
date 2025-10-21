using UnityEngine;

public readonly struct CameraTarget : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(CameraTarget));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(CameraTarget)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}