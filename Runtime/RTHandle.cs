﻿using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class RTHandle
{
    public int Width { get; }
    public int Height { get; }
    public GraphicsFormat Format { get; }
    public bool EnableRandomWrite { get; }
    public int VolumeDepth { get; }
    public TextureDimension Dimension { get; }

    private RenderTexture renderTexture;

    public RTHandle(int width, int height, GraphicsFormat format, bool enableRandomWrite, int volumeDepth, TextureDimension dimension)
    {
        // Depth formats can not be random write
        Assert.IsFalse(enableRandomWrite && GraphicsFormatUtility.IsDepthFormat(format));
        Assert.IsTrue(volumeDepth == 1 || dimension != TextureDimension.Tex2D);

        Width = width;
        Height = height;
        Format = format;
        EnableRandomWrite = enableRandomWrite;
        VolumeDepth = volumeDepth;
        Dimension = dimension;
    }

    public void Create()
    {
        Assert.IsNull(renderTexture);

        if (GraphicsFormatUtility.IsDepthFormat(Format))
            renderTexture = new RenderTexture(Width, Height, GraphicsFormat.None, Format);
        else
        {
            renderTexture = new RenderTexture(Width, Height, Format, GraphicsFormat.None) { enableRandomWrite = EnableRandomWrite };

            if(VolumeDepth > 0)
            {
                renderTexture.dimension = Dimension;
                renderTexture.volumeDepth = VolumeDepth;
            }
        }

        renderTexture.Create();
    }

    public void Release()
    {
        Object.DestroyImmediate(renderTexture);
    }

    public static implicit operator RenderTexture(RTHandle rtHandle)
    {
        return rtHandle.renderTexture;
    }

    public static implicit operator RenderTargetIdentifier(RTHandle rtHandle)
    {
        return rtHandle.renderTexture;
    }

    public static implicit operator RTHandle(RenderTexture renderTexture)
    {
        var result = new RTHandle(renderTexture.width, renderTexture.height, renderTexture.graphicsFormat, renderTexture.enableRandomWrite, renderTexture.volumeDepth, renderTexture.dimension);
        result.renderTexture = renderTexture;
        return result;
    }
}
