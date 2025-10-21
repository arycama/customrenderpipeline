using UnityEngine;

public readonly struct DecalAlbedo : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(DecalAlbedo));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(DecalAlbedo)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}