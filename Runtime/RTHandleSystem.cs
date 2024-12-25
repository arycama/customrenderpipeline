using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RTHandleSystem : IDisposable
{
    private readonly Dictionary<RenderTexture, RTHandle> importedTextures = new();

    private readonly List<(RenderTexture renderTexture, int lastFrameUsed, bool isAvailable, bool isPersistent)> renderTextures = new();
    private readonly Queue<int> availableRtSlots = new();

    private bool disposedValue;
    private int rtCount;
    private int screenWidth, screenHeight;

    private readonly List<RTHandle> rtHandles = new();
    private readonly List<int> createList = new(), freeList = new();

    private readonly List<RTHandle> persistentRtHandles = new();
    private readonly Queue<int> availablePersistentHandleIndices = new();
    private readonly List<int> persistentCreateList = new(), persistentFreeList = new();

    ~RTHandleSystem()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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

        foreach (var rt in renderTextures)
        {
            // Since we don't remove null entries, but rather leave them as "empty", they could be null
            // Also because of the above thing destroying imported textures.. which doesn't really make as much sense, but eh
            if (rt.renderTexture != null)
                Object.DestroyImmediate(rt.renderTexture);
        }

        renderTextures.Clear();
        disposedValue = true;
    }

    public void SetScreenSize(int width, int height)
    {
        screenWidth = Mathf.Max(width, screenWidth);
        screenHeight = Mathf.Max(height, screenHeight);
    }

    public RTHandle GetTexture(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false)
    {
        int index;
        if (isPersistent)
        {
            if (!availablePersistentHandleIndices.TryDequeue(out index))
            {
                index = persistentRtHandles.Count;
                // TODO: Not sure if I like this. This is because we're adding an index that doesn't currently exist. 
                persistentRtHandles.Add(null);
                persistentCreateList.Add(-1);
                persistentFreeList.Add(-1);
            }
        }
        else
        {
            index = rtHandles.Count;
        }

        var result = new RTHandle(index, isPersistent)
        {
            Width = width,
            Height = height,
            Format = format,
            VolumeDepth = volumeDepth,
            Dimension = dimension,
            IsScreenTexture = isScreenTexture,
            HasMips = hasMips,
            AutoGenerateMips = autoGenerateMips,
            // This gets set automatically if a texture is written to by a compute shader
            EnableRandomWrite = false
        };

        if (isPersistent)
        {
            persistentRtHandles[index] = result;
        }
        else
        {
            rtHandles.Add(result);
            createList.Add(-1);
            freeList.Add(-1);
        }

        return result;
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
            IsImported = true,
            IsScreenTexture = false,
            IsCreated = true,
        };

        importedTextures.Add(renderTexture, result);
        return result;
    }

    public void WriteTexture(RTHandle handle, int passIndex)
    {
        // Imported handles don't need create/free logic
        if (handle.IsImported)
            return;

        // Persistent handles that have already been created don't need to write a create-index
        if (handle.IsPersistent && handle.IsCreated)
            return;

        // Select list based on persistent or non-persistent, and initialize or update the index
        var list = handle.IsPersistent ? persistentCreateList : createList;
        var createIndex = list[handle.Index];
        createIndex = createIndex == -1 ? passIndex : Math.Min(passIndex, createIndex);
        list[handle.Index] = createIndex;
    }

    public void ReadTexture(RTHandle handle, int passIndex)
    {
        // Ignore imported textures
        if (handle.IsImported)
            return;

        // Do nothing for non-releasable persistent textures
        if (handle.IsPersistent && handle.IsNotReleasable)
            return;

        var list = handle.IsPersistent ? persistentFreeList : freeList;
        var currentIndex = list[handle.Index];
        currentIndex = currentIndex == -1 ? passIndex : Math.Max(currentIndex, passIndex);
        list[handle.Index] = currentIndex;
    }

    private RenderTexture AssignTexture(RTHandle handle, int frameIndex)
    {
        // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
        RenderTexture result = null;
        for (var j = 0; j < renderTextures.Count; j++)
        {
            var (renderTexture, lastFrameUsed, isAvailable, isPersistent) = renderTextures[j];
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
                renderTextures[j] = (renderTexture, lastFrameUsed, false, handle.IsNotReleasable);
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
                slot = renderTextures.Count;
                renderTextures.Add((result, frameIndex, false, handle.IsNotReleasable));
            }
            else
            {
                renderTextures[slot] = (result, frameIndex, false, handle.IsNotReleasable);
            }

            handle.RenderTextureIndex = slot;
        }

        // Persistent handle no longer needs to be created or cleared. (Non-persistent create list gets cleared every frame)
        if (handle.IsPersistent)
        {
            handle.IsCreated = true;
            persistentCreateList[handle.Index] = -1;
        }

        return result;
    }

    public void FreeTexture(RTHandle handle, int frameIndex)
    {
        renderTextures[handle.RenderTextureIndex] = (handle.RenderTexture, frameIndex, true, false);

        // Hrm
        // If non persistent, no additional logic required since it will be re-created, but persistent needs to free its index
        if (handle.IsPersistent)
        {
            availablePersistentHandleIndices.Enqueue(handle.Index);
            persistentFreeList[handle.Index] = -1; // Set to -1 to indicate this doesn't need to be freed again
        }
    }

    public void AllocateFrameTextures(int renderPassCount, int frameIndex)
    {
        List<List<RTHandle>> texturesToCreate = new();
        List<List<RTHandle>> texturesToFree = new();

        for(var i = 0; i < renderPassCount; i++)
        {
            texturesToCreate.Add(new());
            texturesToFree.Add(new());
        }

        // Non-persistent create/free requests
        for (var i = 0; i < createList.Count; i++)
        {
            var passIndex = createList[i];
            if (passIndex != -1)
                texturesToCreate[passIndex].Add(rtHandles[i]);
        }

        for (var i = 0; i < freeList.Count; i++)
        {
            var passIndex = freeList[i];
            if (passIndex != -1)
                texturesToFree[passIndex].Add(rtHandles[i]);
        }

        // Persistent create/free requests
        for (var i = 0; i < persistentCreateList.Count; i++)
        {
            var passIndex = persistentCreateList[i];
            if (passIndex != -1)
                texturesToCreate[passIndex].Add(persistentRtHandles[i]);
        }

        for (var i = 0; i < persistentFreeList.Count; i++)
        {
            var passIndex = persistentFreeList[i];
            if (passIndex != -1)
                texturesToFree[passIndex].Add(persistentRtHandles[i]);
        }

        for (var i = 0; i < renderPassCount; i++)
        {
            // Assign or create any RTHandles that are written to by this pass
            foreach (var handle in texturesToCreate[i])
            {
                handle.RenderTexture = AssignTexture(handle, frameIndex);
            }

            // Now mark any textures that need to be released at the end of this pass as available
            foreach (var output in texturesToFree[i])
            {
                FreeTexture(output, frameIndex);
            }
        }
    }

    public void CleanupCurrentFrame(int frameIndex)
    {
        // Release any render textures that have not been used for at least a frame
        for (var i = 0; i < renderTextures.Count; i++)
        {
            var renderTexture = renderTextures[i];

            // This indicates it is empty
            if (renderTexture.renderTexture == null)
                continue;

            if (renderTexture.isPersistent)
                continue;

            // Don't free textures that were used in the last frame
            // TODO: Make this a configurable number of frames to avoid rapid re-allocations
            if (renderTexture.lastFrameUsed == frameIndex)
                continue;

            Object.DestroyImmediate(renderTexture.renderTexture);

            // Fill this with a null, unavailable RT and add the index to a list
            renderTextures[i] = (null, renderTexture.lastFrameUsed, false, false);
            availableRtSlots.Enqueue(i);
        }

        rtHandles.Clear();
        createList.Clear();
        freeList.Clear();
    }
}