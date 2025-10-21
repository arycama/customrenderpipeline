using UnityEngine;

public readonly struct PreviousCameraVelocity : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(PreviousCameraVelocity));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(PreviousCameraVelocity)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}