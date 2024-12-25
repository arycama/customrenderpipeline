using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RTHandleSystem : ResourceHandleSystem<RenderTexture, RTHandle>
{
    private int screenWidth, screenHeight;

    public void SetScreenSize(int width, int height)
    {
        screenWidth = Mathf.Max(width, screenWidth);
        screenHeight = Mathf.Max(height, screenHeight);
    }

    public RTHandle GetResourceHandle(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false)
    {
        int index;
        if (isPersistent)
        {
            if (!availablePersistentHandleIndices.TryDequeue(out index))
            {
                index = persistentHandles.Count;
                // TODO: Not sure if I like this. This is because we're adding an index that doesn't currently exist. 
                persistentHandles.Add(null);
                persistentCreateList.Add(-1);
                persistentFreeList.Add(-1);
            }
        }
        else
        {
            index = handles.Count;
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
            persistentHandles[index] = result;
        }
        else
        {
            handles.Add(result);
            createList.Add(-1);
            freeList.Add(-1);
        }

        return result;
    }

    protected override RTHandle CreateHandleFromResource(RenderTexture resource)
    {
        // Ensure its created (Can happen with some RenderTextures that are imported as soon as created
        if (!resource.IsCreated())
            _ = resource.Create();

        return new RTHandle(-1, true)
        {
            Width = resource.width,
            Height = resource.height,
            Format = resource.graphicsFormat,
            EnableRandomWrite = resource.enableRandomWrite,
            VolumeDepth = resource.volumeDepth,
            Dimension = resource.dimension,
            Resource = resource,
            HasMips = resource.useMipMap,
            AutoGenerateMips = resource.autoGenerateMips,
            IsImported = true,
            IsScreenTexture = false,
            IsCreated = true,
        };
    }

    protected override RenderTexture AssignResource(RTHandle handle, int frameIndex)
    {
        // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
        int slot = -1;
        RenderTexture result = null;
        for (var j = 0; j < resources.Count; j++)
        {
            var (renderTexture, lastFrameUsed, isAvailable) = resources[j];
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
                slot = j;
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

            result.name = $"{result.dimension} {(isDepth ? result.depthStencilFormat : result.graphicsFormat)} {width}x{height} {resourceCount++}";
            _ = result.Create();

            // Get a slot for this render texture if possible
            if (!availableSlots.TryDequeue(out slot))
            {
                slot = resources.Count;
                resources.Add(default);
            }
        }

        handle.ResourceIndex = slot;

        // Persistent handle no longer needs to be created or cleared. (Non-persistent create list gets cleared every frame)
        if (handle.IsPersistent)
        {
            handle.IsCreated = true;
            persistentCreateList[handle.HandleIndex] = -1;
        }

        resources[slot] = (result, frameIndex, false);
        return result;
    }

    protected override void DestroyResource(RenderTexture resource)
    {
        Object.DestroyImmediate(resource);
    }
}