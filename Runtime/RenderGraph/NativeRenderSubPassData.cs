using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class NativeRenderSubPassData
{
    public Int3 size;
    public AttachmentDescriptor? depthAttachment;
    public SubPassFlags flags;
    public readonly List<AttachmentDescriptor> colorAttachments = new();
    public readonly List<int> subPassOutputs = new();

    public void SetSize(Int3 size)
    {
        this.size = size;
    }

    public void SetSubPassFlags(SubPassFlags flags)
    {
        this.flags = flags;
    }

    public void SetDepthTarget(GraphicsFormat format, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, RenderTargetIdentifier loadStoreTarget)
    {
        depthAttachment = new AttachmentDescriptor(format) { loadAction = loadAction, storeAction = storeAction, loadStoreTarget = loadStoreTarget };
    }

    public void AddAttachment(GraphicsFormat format, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, RenderTargetIdentifier loadStoreTarget, Color clearColor = default)
    {
        subPassOutputs.Add(colorAttachments.Count);
        colorAttachments.Add(new AttachmentDescriptor(format) { loadAction = loadAction, storeAction = storeAction, loadStoreTarget = loadStoreTarget, clearColor = clearColor });
    }

    public void Reset()
    {
        size = 0;
        colorAttachments.Clear();
        depthAttachment = null;
        subPassOutputs.Clear();
    }

    /// <summary>
    /// Checks if this pass can merge with another pass, possibly as a subpass
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool CanMergeWithPass(NativeRenderSubPassData other)
    {
        // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
        return size == other.size &&
            depthAttachment.HasValue == other.depthAttachment.HasValue &&
            depthAttachment.Value.loadStoreTarget == other.depthAttachment.Value.loadStoreTarget;
    }

    public bool CanMergeWithSubPass(NativeRenderSubPassData other)
    {
        // A subpass can only merge with another sub pass if they have the exact same flags, color attachment count -and- output indices
        if (flags != other.flags || colorAttachments.Count != other.colorAttachments.Count)
            return false;

        for (var i = 0; i < colorAttachments.Count; i++)
        {
            if (colorAttachments[i].loadStoreTarget != other.colorAttachments[i].loadStoreTarget)
                return false;
        }

        return true;
    }

    public bool CanMergeWithPassAndSubPass(NativeRenderSubPassData other)
    {
        return CanMergeWithPass(other) && CanMergeWithSubPass(other);
    }
}