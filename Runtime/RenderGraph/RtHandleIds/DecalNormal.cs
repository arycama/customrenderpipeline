using UnityEngine;

public readonly struct DecalNormal : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(DecalNormal));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(DecalNormal)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}