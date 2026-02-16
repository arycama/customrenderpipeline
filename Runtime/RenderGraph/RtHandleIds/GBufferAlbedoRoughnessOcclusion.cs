using UnityEngine;

public readonly struct GBufferAlbedoRoughnessOcclusion : IRtHandleId
{
    public static readonly int PropertyId = Shader.PropertyToID(nameof(GBufferAlbedoRoughnessOcclusion));
    public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(GBufferAlbedoRoughnessOcclusion)}ScaleLimit");

    readonly int IRtHandleId.PropertyId => PropertyId;
    readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}
