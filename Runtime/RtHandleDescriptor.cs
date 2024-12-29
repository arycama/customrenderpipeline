using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public readonly struct RtHandleDescriptor
{
    public int Width { get; }
    public int Height { get; }
    public GraphicsFormat Format { get; }
    public int VolumeDepth { get; }
    public TextureDimension Dimension { get; }
    public bool IsScreenTexture { get; }
    public bool HasMips { get; }
    public bool AutoGenerateMips { get; }

    public RtHandleDescriptor(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false)
    {
        Width = width;
        Height = height;
        Format = format;
        VolumeDepth = volumeDepth;
        Dimension = dimension;
        IsScreenTexture = isScreenTexture;
        HasMips = hasMips;
        AutoGenerateMips = autoGenerateMips;
    }
}
