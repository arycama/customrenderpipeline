﻿using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public readonly struct RtHandleDescriptor : IResourceDescriptor<RenderTexture>
{
    public int Width { get; }
    public int Height { get; }
    public GraphicsFormat Format { get; }
    public int VolumeDepth { get; }
    public TextureDimension Dimension { get; }
    public bool IsScreenTexture { get; }
    public bool HasMips { get; }
    public bool AutoGenerateMips { get; }
    public bool EnableRandomWrite { get; }
    public bool IsExactSize { get; }

    public RtHandleDescriptor(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool enableRandomWrite = false, bool isExactSize = false)
    {
        Width = width;
        Height = height;
        Format = format;
        VolumeDepth = volumeDepth;
        Dimension = dimension;
        IsScreenTexture = isScreenTexture;
        HasMips = hasMips;
        AutoGenerateMips = autoGenerateMips;
        EnableRandomWrite = enableRandomWrite;
        IsExactSize = isExactSize;
    }

    public RenderTexture CreateResource(ResourceHandleSystem system)
    {
        var rtHandleSystem = system as RTHandleSystem;
        var (width, height) = IsScreenTexture ? (rtHandleSystem.ScreenWidth, rtHandleSystem.ScreenHeight) : (Width, Height);
        var isDepth = GraphicsFormatUtility.IsDepthFormat(Format);
        var isStencil = GraphicsFormatUtility.IsStencilFormat(Format);
        var graphicsFormat = isDepth ? GraphicsFormat.None : Format;
        var depthFormat = isDepth ? Format : GraphicsFormat.None;
        var stencilFormat = isStencil ? GraphicsFormat.R8_UInt : GraphicsFormat.None;

        var result = new RenderTexture(width, height, graphicsFormat, depthFormat)
        {
            autoGenerateMips = false, // Always false, we manually handle mip generation if needed
            dimension = Dimension,
            enableRandomWrite = EnableRandomWrite,
            hideFlags = HideFlags.HideAndDontSave,
            stencilFormat = stencilFormat,
            useMipMap = HasMips,
            volumeDepth = VolumeDepth,
        };

        if(!result.Create())
        {
            Debug.LogError($"{width} {height} {VolumeDepth}");
        }

        return result;
    }
}
