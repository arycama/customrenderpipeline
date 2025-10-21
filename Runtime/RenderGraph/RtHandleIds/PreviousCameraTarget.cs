using UnityEngine;

public readonly struct PreviousCameraTarget : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(PreviousCameraTarget));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(PreviousCameraTarget)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}