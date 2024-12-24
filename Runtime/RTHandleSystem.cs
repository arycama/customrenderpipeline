using Arycama.CustomRenderPipeline;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RTHandleSystem : IDisposable
{
    private readonly RenderGraph renderGraph;
    private readonly Dictionary<RenderTexture, RTHandle> importedTextures = new();

    private readonly List<(RenderTexture renderTexture, int lastFrameUsed, bool isAvailable, bool isPersistent)> availableRenderTextures = new();
    private readonly HashSet<RenderTexture> allRenderTextures = new();
    private readonly Queue<int> availableRtSlots = new();

    private int rtHandleCount;
    private bool disposedValue;
    private int rtCount;
    private int screenWidth, screenHeight;

    public readonly List<int> lastRtHandleRead = new();
    public List<RTHandle> rtHandles = new();

    public List<RTHandle> persistentRtHandles = new();
    public Queue<int> persistentRtHandleFreeIndices = new();
    public readonly Dictionary<RTHandle, int> lastPersistentRtHandleRead = new();

    public RTHandleSystem(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
    }

    public RTHandle GetTexture(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false)
    {
        int index;
        if(!isPersistent)
        {
            index = rtHandles.Count;
        }
        else
        {
            if (!persistentRtHandleFreeIndices.TryDequeue(out index))
            {
                index = persistentRtHandles.Count;
                persistentRtHandles.Add(null); // TODO: Not sure if I like this. This is because we're adding an index that doesn't currently exist. 
            }
        }

        var result = new RTHandle(index, isPersistent)
        {
            Id = rtHandleCount++,
            Width = width,
            Height = height,
            Format = format,
            VolumeDepth = volumeDepth,
            Dimension = dimension,
            IsScreenTexture = isScreenTexture,
            HasMips = hasMips,
            AutoGenerateMips = autoGenerateMips,
            IsAssigned = !isPersistent,
            // This gets set automatically if a texture is written to by a compute shader
            EnableRandomWrite = false
        };

        if (!isPersistent)
        {
            rtHandles.Add(result);
            lastRtHandleRead.Add(-1);
        }
        else
        {
            persistentRtHandles[index] = result;
        }

        return result;
    }

    public void SetScreenSize(int width, int height)
    {
        screenWidth = Mathf.Max(width, screenWidth);
        screenHeight = Mathf.Max(height, screenHeight);
    }

    public RTHandle ImportRenderTexture(RenderTexture renderTexture, bool autoGenerateMips = false)
    {
        if (importedTextures.TryGetValue(renderTexture, out var result))
            return result;

        // Ensure its created (Can happen with some RenderTextures that are imported as soon as created
        if (!renderTexture.IsCreated())
            _ = renderTexture.Create();

        result = new RTHandle(-1, true)
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
            Id = rtHandleCount++,
            IsImported = true,
            IsScreenTexture = false,
            IsAssigned = true,
        };

        importedTextures.Add(renderTexture, result);
        allRenderTextures.Add(result);

        return result;
    }

    public void MakeTextureAvailable(RTHandle handle, int frameIndex)
    {
        availableRenderTextures[handle.RenderTextureIndex] = (handle.RenderTexture, frameIndex, true, false);

        // Hrm
        // If non persistent, no additional logic required since it will be re-created, but persistent needs to free its index
        if(handle.IsPersistentInternal)
        {
            persistentRtHandleFreeIndices.Enqueue(handle.Index);
        }
    }

    public RenderTexture GetTexture(RTHandle handle, int FrameIndex)
    {
        // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
        RenderTexture result = null;
        for (var j = 0; j < availableRenderTextures.Count; j++)
        {
            var (renderTexture, lastFrameUsed, isAvailable, isPersistent) = availableRenderTextures[j];
            if (!isAvailable)
                continue;

            var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
            if ((isDepth && handle.Format != renderTexture.depthStencilFormat) || (!isDepth && handle.Format != renderTexture.graphicsFormat))
                continue;

           if (handle.IsScreenTexture)
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

            // Get a slot for this render texture if possible
            if (!availableRtSlots.TryDequeue(out var slot))
            {
                slot = availableRenderTextures.Count;
                availableRenderTextures.Add((result, FrameIndex, false, handle.IsPersistent));
            }
            else
            {
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

        lastRtHandleRead.Clear();
        lastPersistentRtHandleRead.Clear();
        rtHandles.Clear();
    }

    public void SetLastRTHandleRead(RTHandle handle, int passIndex)
    {
        // Persistent handles must be freed using release persistent texture
        if (handle.IsPersistent)
            return;

        // Also don't add imported textures since they don't need to be allocated/released this way
        if (handle.IsImported)
            return;

        // Handles that were persistent but not anymore use a different index
        if (handle.IsPersistentInternal)
            lastPersistentRtHandleRead[handle] = passIndex;
        else
            lastRtHandleRead[handle.Index] = passIndex;
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