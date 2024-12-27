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
        int handleIndex;
        if (isPersistent)
        {
            if (!availablePersistentHandleIndices.TryDequeue(out handleIndex))
            {
                handleIndex = persistentHandles.Count;
                persistentHandles.Add(null);
                persistentResourceIndices.Add(-1);
                persistentCreateList.Add(-1);
                persistentFreeList.Add(-1);
            }
        }
        else
        {
            handleIndex = handles.Count;
        }

        var result = new RTHandle(handleIndex, false, isPersistent)
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
            persistentHandles[handleIndex] = result;
        }
        else
        {
            handles.Add(result);
            resourceIndices.Add(-1);
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

        return new RTHandle(-1, true, true)
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
            IsScreenTexture = false,
        };
    }

    protected override bool DoesResourceMatchHandle(RenderTexture resource, RTHandle handle)
    {
        var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
        if ((isDepth && handle.Format != resource.depthStencilFormat) || (!isDepth && handle.Format != resource.graphicsFormat))
            return false;

        if (handle.IsScreenTexture)
        {
            // For screen textures, ensure we get a rendertexture that is the actual screen width/height
            if (resource.width != screenWidth || resource.height != screenHeight)
                return false;
        }
        else if (resource.width < handle.Width || resource.height < handle.Height)
            return false;

        if (resource.enableRandomWrite == handle.EnableRandomWrite && resource.dimension == handle.Dimension && resource.useMipMap == handle.HasMips)
        {
            if (handle.Dimension != TextureDimension.Tex2D && resource.volumeDepth < handle.VolumeDepth)
                return false;

            return true;
        }

        return false;
    }

    protected override RenderTexture CreateResource(RTHandle handle)
    {
        var isDepth = GraphicsFormatUtility.IsDepthFormat(handle.Format);
        var isStencil = handle.Format == GraphicsFormat.D32_SFloat_S8_UInt || handle.Format == GraphicsFormat.D24_UNorm_S8_UInt;

        var width = handle.IsScreenTexture ? screenWidth : handle.Width;
        var height = handle.IsScreenTexture ? screenHeight : handle.Height;

        var result = new RenderTexture(width, height, isDepth ? GraphicsFormat.None : handle.Format, isDepth ? handle.Format : GraphicsFormat.None) { enableRandomWrite = handle.EnableRandomWrite, stencilFormat = isStencil ? GraphicsFormat.R8_UInt : GraphicsFormat.None, hideFlags = HideFlags.HideAndDontSave };

        if (handle.VolumeDepth > 0)
        {
            result.dimension = handle.Dimension;
            result.volumeDepth = handle.VolumeDepth;
            result.useMipMap = handle.HasMips;
            result.autoGenerateMips = false; // Always false, we manually handle mip generation if needed
        }

        result.name = $"{result.dimension} {(isDepth ? result.depthStencilFormat : result.graphicsFormat)} {width}x{height} {resourceCount++}";
        _ = result.Create();

        return result;
    }

    protected override void DestroyResource(RenderTexture resource)
    {
        Object.DestroyImmediate(resource);
    }
}