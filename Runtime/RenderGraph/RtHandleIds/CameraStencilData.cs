using UnityEngine;

public readonly struct CameraStencil : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(CameraStencil));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(CameraStencil)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}