using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class RTHandle
{
    public int Id { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public GraphicsFormat Format { get; set; }
    public bool EnableRandomWrite { get; set; }
    public int VolumeDepth { get; set; }
    public TextureDimension Dimension { get; set; }
    public bool IsImported { get; set; }
    public Vector2 Scale { get; set; }

    public RenderTexture RenderTexture { get; set; }

    public static implicit operator RenderTexture(RTHandle rtHandle)
    {
        return rtHandle.RenderTexture;
    }

    public static implicit operator RenderTargetIdentifier(RTHandle rtHandle)
    {
        return rtHandle.RenderTexture;
    }

    public static implicit operator RTHandle(RenderTexture renderTexture)
    {
        return new RTHandle()
        {
            Width = renderTexture.width,
            Height = renderTexture.height,
            Format = renderTexture.graphicsFormat,
            EnableRandomWrite = renderTexture.enableRandomWrite,
            VolumeDepth = renderTexture.volumeDepth,
            Dimension = renderTexture.dimension,
            RenderTexture = renderTexture,
            Scale = Vector2.one
        };
    }

    public override string ToString()
    {
        return $"RTHandle {Id} {Dimension} {Format} {Width}x{Height} ";
    }
}
