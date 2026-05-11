using UnityEngine.Rendering;

/// <summary>
/// Utility class for an IRenderPassData that contains a single ResourceHandle<RenderTexture>
/// </summary>
public readonly struct RTHandleData
{
    public readonly int propertyNameId, scaleLimitPropertyId;
    public readonly int mip;
    public readonly RenderTextureSubElement subElement;

    public RTHandleData(int propertyNameId, int scaleLimitPropertyId, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
    {
        this.mip = mip;
        this.subElement = subElement;
        this.propertyNameId = propertyNameId;
        this.scaleLimitPropertyId = scaleLimitPropertyId;
    }
}
