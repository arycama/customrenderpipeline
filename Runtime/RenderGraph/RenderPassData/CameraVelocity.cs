using UnityEngine;

public readonly struct CameraVelocity : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(CameraVelocity));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(CameraVelocity)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}