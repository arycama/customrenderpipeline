using UnityEngine;

public readonly struct GBufferNormalMetallic : IRtHandleId
{
    public static readonly int PropertyId = Shader.PropertyToID(nameof(GBufferNormalMetallic));
    public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(GBufferNormalMetallic)}ScaleLimit");

    readonly int IRtHandleId.PropertyId => PropertyId;
    readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}