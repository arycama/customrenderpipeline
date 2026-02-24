using UnityEngine;

public readonly struct GBufferAlbedoOcclusion : IRtHandleId
{
    public static readonly int PropertyId = Shader.PropertyToID(nameof(GBufferAlbedoOcclusion));
    public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(GBufferAlbedoOcclusion)}ScaleLimit");

    readonly int IRtHandleId.PropertyId => PropertyId;
    readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}
