using UnityEngine;

public readonly struct GizmosTarget : IRtHandleId
{
    public static readonly int PropertyId = Shader.PropertyToID(nameof(GizmosTarget));
    public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(GizmosTarget)}ScaleLimit");

    readonly int IRtHandleId.PropertyId => PropertyId;
    readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}