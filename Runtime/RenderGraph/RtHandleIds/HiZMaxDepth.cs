using UnityEngine;

public readonly struct HiZMaxDepth : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(HiZMaxDepth));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(HiZMaxDepth)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}