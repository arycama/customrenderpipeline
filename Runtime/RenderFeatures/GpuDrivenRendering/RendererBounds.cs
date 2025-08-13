using UnityEngine;

public struct RendererBounds
{
    public Vector3 center;
    public float pad0;
    public Vector3 extents;
    public float pad1;

    public RendererBounds(Bounds bounds)
    {
        center = bounds.Min;
        pad0 = 0;
        extents = bounds.Size;
        pad1 = 0;
    }
}