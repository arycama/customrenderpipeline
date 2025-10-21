using UnityEngine;

public readonly struct GBufferAlbedoMetallic : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(GBufferAlbedoMetallic));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(GBufferAlbedoMetallic)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}