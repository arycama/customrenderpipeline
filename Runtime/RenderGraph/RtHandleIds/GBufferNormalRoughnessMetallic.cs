using UnityEngine;

public readonly struct GBufferNormalRoughnessMetallic : IRtHandleId
{
    public static readonly int PropertyId = Shader.PropertyToID(nameof(GBufferNormalRoughnessMetallic));
    public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(GBufferNormalRoughnessMetallic)}ScaleLimit");

    readonly int IRtHandleId.PropertyId => PropertyId;
    readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}