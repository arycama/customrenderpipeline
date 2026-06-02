using Unmath;

public struct RendererBounds
{
    public Float3 min;
    public float pad0;
    public Float3 size;
    public float pad1;

    public RendererBounds(Bounds bounds)
    {
		min = bounds.Min;
        pad0 = 0;
		size = bounds.Size;
        pad1 = 0;
    }
}