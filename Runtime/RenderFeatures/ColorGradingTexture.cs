using UnityEngine;

public readonly struct ColorGradingTexture : IRtHandleId
{
    public static readonly int PropertyId = Shader.PropertyToID(nameof(ColorGradingTexture));
    public static readonly int ScaleLimitPropertyId = Shader.PropertyToID($"{nameof(ColorGradingTexture)}ScaleLimit");

    readonly int IRtHandleId.PropertyId => PropertyId;
    readonly int IRtHandleId.ScaleLimitPropertyId => ScaleLimitPropertyId;
}