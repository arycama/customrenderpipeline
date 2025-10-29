using UnityEngine;

public readonly struct VirtualTerrainFeedback : IRtHandleId
{
	public static readonly int PropertyId = Shader.PropertyToID(nameof(VirtualTerrainFeedback));
	public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(VirtualTerrainFeedback)}ScaleLimit");

	readonly int IRtHandleId.PropertyId => PropertyId;
	readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}