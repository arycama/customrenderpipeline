using UnityEngine;

public readonly struct HiZMinDepth : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(HiZMinDepth));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(HiZMinDepth)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}