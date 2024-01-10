using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class RTHandle
{
    public int Width { get; }
    public int Height { get; }
    public GraphicsFormat Format { get; }

    private RenderTexture renderTexture;

    public RTHandle(int width, int height, GraphicsFormat format)
    {
        Width = width;
        Height = height;
        Format = format;
    }

    public void Reset(int width, int height)
    {

    }
}
