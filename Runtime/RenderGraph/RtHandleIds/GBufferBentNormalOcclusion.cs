using UnityEngine;

public readonly struct GBufferBentNormalOcclusion : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(GBufferBentNormalOcclusion));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(GBufferBentNormalOcclusion)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}