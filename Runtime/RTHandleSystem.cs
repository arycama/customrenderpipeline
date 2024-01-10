using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class RTHandleSystem
{
    private Dictionary<GraphicsFormat, List<RTHandle>> formatHandles = new();

    private int maxWidth, maxHeight;

    public float ScaleX { get; private set; } = 1.0f;
    public float ScaleY { get; private set; } = 1.0f;

    public void SetResolution(int width, int height)
    {
        if(width > maxWidth || height > maxHeight)
        {
            maxWidth = Mathf.Max(width, maxWidth);
            maxHeight = Mathf.Max(height, maxHeight);

            ReallocateAllTargets();
        }

        ScaleX = width / (float)maxWidth;
        ScaleY = height / (float)maxHeight;
    }

    private void ReallocateAllTargets()
    {
        foreach(var list in formatHandles.Values)
        {
            foreach(var handle in list)
            {
                //handle.Reset(maxWidth, maxHeight);
            }
        }
    }

    public RTHandle GetHandle(int width, int height, GraphicsFormat format)
    {
        if(!formatHandles.TryGetValue(format, out var rtHandleList))
        {
            rtHandleList = new List<RTHandle>();
            formatHandles.Add(format, rtHandleList);
        }

        for (var i = 0; i < rtHandleList.Count; i++)
        {
            var rtHandle = rtHandleList[i];
            if (rtHandle.Width >= width || rtHandle.Height >= height)
                continue;

            rtHandleList.RemoveAt(i);
            return rtHandle;
        }

        return new RTHandle(width, height, format, false, 0, TextureDimension.Tex2D);
    }

    public void ReleaseHandle(RTHandle handle)
    {
        var rtHandleList = formatHandles[handle.Format];
        rtHandleList.Add(handle);
    }
}
