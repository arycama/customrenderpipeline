using System.Collections.Generic;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public delegate void RenderGraphPass(CommandBuffer command, ScriptableRenderContext context);

public class RenderGraph
{
    private List<RenderGraphPass> actions = new();
    private List<RTHandle> handlesToCreate = new();
    private List<RTHandle> availableHandles = new();
    private List<RTHandle> usedHandles = new();

    private bool isExecuting;

    public void AddRenderPass(RenderGraphPass pass)
    {
        actions.Add(pass);
    }

    public void Execute(CommandBuffer command, ScriptableRenderContext context)
    {
        // Create all RTs.
        foreach(var handle in handlesToCreate)
        {
            handle.Create();
        }

        handlesToCreate.Clear();

        isExecuting = true;
        try
        {
            foreach (var action in actions)
            {
                action(command, context);
            }
        }
        finally
        {
            isExecuting = false;
        }

        actions.Clear();
    }

    public RTHandle GetTexture(int width, int height, GraphicsFormat format, bool enableRandomWrite = false, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D)
    {
        // Ensure we're not getting a texture during execution, this must be done in the setup
        Assert.IsFalse(isExecuting);

        // Find first handle that matches width, height and format (TODO: Allow returning a texture with larger width or height, plus a scale factor)
        for (var i = 0; i < availableHandles.Count; i++)
        {
            var handle = availableHandles[i];
            if (handle.Width == width && handle.Height == height && handle.Format == format && handle.EnableRandomWrite == enableRandomWrite && handle.VolumeDepth == volumeDepth && handle.Dimension == dimension)
            {
                availableHandles.RemoveAt(i);
                usedHandles.Add(handle);
                return handle;
            }
        }

        // If no handle was found, create a new one, and assign it as one to be created. 
        var result = new RTHandle(width, height, format, enableRandomWrite, volumeDepth, dimension);
        handlesToCreate.Add(result);
        usedHandles.Add(result);
        return result;
    }

    public void ReleaseRTHandles()
    {
        // Mark all RTHandles as available for use again
        foreach(var handle in usedHandles)
            availableHandles.Add(handle);

        usedHandles.Clear();
    }

    public void Release()
    {
        foreach (var handle in availableHandles)
            handle.Release();

        availableHandles.Clear();
    }
}
