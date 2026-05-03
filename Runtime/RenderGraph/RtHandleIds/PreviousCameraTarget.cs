using UnityEngine;

public readonly struct SceneColor : IRtHandleId
{
    public static readonly int PropertyId = Shader.PropertyToID(nameof(SceneColor));
    public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(SceneColor)}ScaleLimit");

    readonly int IRtHandleId.PropertyId => PropertyId;
    readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}

public readonly struct SceneColorCopy : IRtHandleId
{
    public static readonly int PropertyId = Shader.PropertyToID(nameof(SceneColorCopy));
    public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(SceneColorCopy)}ScaleLimit");

    readonly int IRtHandleId.PropertyId => PropertyId;
    readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}