using UnityEngine;

public readonly struct GBufferNormalRoughness : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(GBufferNormalRoughness));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(GBufferNormalRoughness)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}