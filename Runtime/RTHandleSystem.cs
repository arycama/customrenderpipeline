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
    private readonly List<(RenderTexture renderTexture, int lastFrameUsed, bool isAvailable, bool isPersistent)> availableRenderTextures = new();
    private readonly HashSet<RenderTexture> allRenderTextures = new();
    private readonly Queue<int> availableRtSlots = new();
    private int rtCount;

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

        allRenderTextures.Add(result);

        return result;
    }

    public void ReleaseImportedTexture(RenderTexture texture)
    {
        var wasRemoved = importedTextures.Remove(texture);
        Assert.IsTrue(wasRemoved, "Trying to release a non-imported texture");
    }

    public void MakeTextureAvailable(RTHandle handle, int frameIndex)
    {
        availableRenderTextures[handle.RenderTextureIndex] = (handle.RenderTexture, frameIndex, true, false);
        availableRtHandles.Enqueue(handle);
    }

    public RenderTexture GetTexture(RTHandle handle, int FrameIndex, int screenWidth, int screenHeight)
    {
        // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
        RenderTexture result = null;
        for (var j = 0; j < availableRenderTextures.Count; j++)
        {
            var (renderTexture, lastFrameUsed, isAvailable, isPersistent) = availableRenderTextures[j];
            if (!isAvailable)
                continue;

            var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
            Assert.IsNotNull(handle, "Handle is null in pass");
            Assert.IsNotNull(renderTexture, "renderTexture is null in pass");
            if ((isDepth && handle.Format != renderTexture.depthStencilFormat) || (!isDepth && handle.Format != renderTexture.graphicsFormat))
                continue;

            // TODO: Use some enum instead?
            if (handle.IsExactSize)
            {
                if (renderTexture.width != handle.Width || renderTexture.height != handle.Height)
                    continue;
            }
            else if (handle.IsScreenTexture)
            {
                // For screen textures, ensure we get a rendertexture that is the actual screen width/height
                if (renderTexture.width != screenWidth || renderTexture.height != screenHeight)
                    continue;
            }
            else if (renderTexture.width < handle.Width || renderTexture.height < handle.Height)
                continue;

            if (renderTexture.enableRandomWrite == handle.EnableRandomWrite && renderTexture.dimension == handle.Dimension && renderTexture.useMipMap == handle.HasMips)
            {
                if (handle.Dimension != TextureDimension.Tex2D && renderTexture.volumeDepth < handle.VolumeDepth)
                    continue;

                result = renderTexture;
                Assert.IsNotNull(renderTexture);
                Assert.IsTrue(renderTexture.IsCreated());
                availableRenderTextures[j] = (renderTexture, lastFrameUsed, false, handle.IsPersistent);
                handle.RenderTextureIndex = j;
                break;
            }
        }

        if (result == null)
        {
            var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
            var isStencil = handle.Format == GraphicsFormat.D32_SFloat_S8_UInt || handle.Format == GraphicsFormat.D24_UNorm_S8_UInt;

            var width = handle.IsScreenTexture ? screenWidth : handle.Width;
            var height = handle.IsScreenTexture ? screenHeight : handle.Height;

            result = new RenderTexture(width, height, isDepth ? GraphicsFormat.None : handle.Format, isDepth ? handle.Format : GraphicsFormat.None) { enableRandomWrite = handle.EnableRandomWrite, stencilFormat = isStencil ? GraphicsFormat.R8_UInt : GraphicsFormat.None, hideFlags = HideFlags.HideAndDontSave };
            allRenderTextures.Add(result);

            if (handle.VolumeDepth > 0)
            {
                result.dimension = handle.Dimension;
                result.volumeDepth = handle.VolumeDepth;
                result.useMipMap = handle.HasMips;
                result.autoGenerateMips = false; // Always false, we manually handle mip generation if needed
            }

            result.name = $"{result.dimension} {(isDepth ? result.depthStencilFormat : result.graphicsFormat)} {width}x{height} {rtCount++}";
            _ = result.Create();

            //Debug.Log($"Allocating {result.name}");

            // Get a slot for this render texture if possible
            if (!availableRtSlots.TryDequeue(out var slot))
            {
                slot = availableRenderTextures.Count;
                Assert.IsNotNull(result);
                availableRenderTextures.Add((result, FrameIndex, false, handle.IsPersistent));
            }
            else
            {
                Assert.IsNotNull(result);
                availableRenderTextures[slot] = (result, FrameIndex, false, handle.IsPersistent);
            }

            handle.RenderTextureIndex = slot;
        }

        return result;
    }

    public void FreeThisFramesTextures(int frameIndex)
    {
        // Release any render textures that have not been used for at least a frame
        for (var i = 0; i < availableRenderTextures.Count; i++)
        {
            var renderTexture = availableRenderTextures[i];

            // This indicates it is empty
            if (renderTexture.renderTexture == null)
                continue;

            if (renderTexture.isPersistent)
                continue;

            // Don't free textures that were used in the last frame
            // TODO: Make this a configurable number of frames to avoid rapid re-allocations
            if (renderTexture.lastFrameUsed == frameIndex)
                continue;

            allRenderTextures.Remove(renderTexture.renderTexture);
            Object.DestroyImmediate(renderTexture.renderTexture);

            // Fill this with a null, unavailable RT and add the index to a list
            availableRenderTextures[i] = (null, renderTexture.lastFrameUsed, false, false);
            availableRtSlots.Enqueue(i);
        }
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

        foreach (var rt in allRenderTextures)
        {
            // Since we don't remove null entries, but rather leave them as "empty", they could be null
            // Also because of the above thing destroying imported textures.. which doesn't really make as much sense, but eh
            if (rt != null)
                Object.DestroyImmediate(rt);
        }

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