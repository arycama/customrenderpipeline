using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RTHandleSystem : IDisposable
{
    private readonly Dictionary<RenderTexture, RTHandle> importedTextures = new();
    private int rtHandleCount;
    private bool disposedValue;
    private readonly Queue<RTHandle> availableRtHandles = new();

    public RTHandle GetTexture(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false, bool isExactSize = false)
    {
        // Ensure we're not getting a texture during execution, this must be done in the setup

        if (!availableRtHandles.TryDequeue(out var result))
        {
            result = new RTHandle
            {
                Id = rtHandleCount++
            };
        }

        result.Width = width;
        result.Height = height;
        result.Format = format;
        result.VolumeDepth = volumeDepth;
        result.Dimension = dimension;
        result.IsScreenTexture = isScreenTexture;
        result.HasMips = hasMips;
        result.AutoGenerateMips = autoGenerateMips;
        result.IsPersistent = isPersistent;
        result.IsAssigned = isPersistent ? false : true;
        result.IsExactSize = isExactSize;

        // This gets set automatically if a texture is written to by a compute shader
        result.EnableRandomWrite = false;

        return result;
    }

    public RTHandle ImportRenderTexture(RenderTexture renderTexture, bool autoGenerateMips = false)
    {
        if (importedTextures.TryGetValue(renderTexture, out var result))
            return result;

        // Ensure its created (Can happen with some RenderTextures that are imported as soon as created
        if (!renderTexture.IsCreated())
            _ = renderTexture.Create();

        result = new RTHandle
        {
            Width = renderTexture.width,
            Height = renderTexture.height,
            Format = renderTexture.graphicsFormat,
            EnableRandomWrite = renderTexture.enableRandomWrite,
            VolumeDepth = renderTexture.volumeDepth,
            Dimension = renderTexture.dimension,
            RenderTexture = renderTexture,
            HasMips = renderTexture.useMipMap,
            AutoGenerateMips = autoGenerateMips,
            Id = rtHandleCount++
        };
        importedTextures.Add(renderTexture, result);
        result.IsImported = true;
        result.IsScreenTexture = false;
        result.IsAssigned = true;
        result.IsExactSize = true;

        return result;
    }

    public void ReleaseImportedTexture(RenderTexture texture)
    {
        var wasRemoved = importedTextures.Remove(texture);
        Assert.IsTrue(wasRemoved, "Trying to release a non-imported texture");
    }

    public void MakeTextureAvailable(RTHandle handle)
    {
        availableRtHandles.Enqueue(handle);
    }


    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue)
        {
            Debug.LogError("Disposing an already disposed RTHandleSystem");
            return;
        }

        if (!disposing)
            Debug.LogError("RT Handle System not disposed correctly");

        foreach (var importedRT in importedTextures)
            Object.DestroyImmediate(importedRT.Key);

        disposedValue = true;
    }

    ~RTHandleSystem()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}