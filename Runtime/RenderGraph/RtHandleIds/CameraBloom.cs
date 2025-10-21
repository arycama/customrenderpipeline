using UnityEngine;

public readonly struct CameraBloom : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(CameraBloom));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(CameraBloom)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}