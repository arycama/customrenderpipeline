using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Rendering;

public class RenderPassData
{
    public Int3 size;
    public AttachmentDescriptor? depthAttachment;
    public readonly List<AttachmentDescriptor> colorAttachments = new();
    public NativeList<int> inputs = new NativeList<int>(8, Allocator.Persistent), outputs = new NativeList<int>(8, Allocator.Persistent);
    public SubPassFlags flags;

    public void SetSize(Int3 size)
    {
        this.size = size;
    }

    public void SetSubPassFlags(SubPassFlags flags)
    {
        this.flags = flags;
    }

    public void WriteDepth(AttachmentDescriptor attachment)
    {
        depthAttachment = attachment;
    }

    public void WriteColor(AttachmentDescriptor attachment)
    {
        var index = colorAttachments.FindIndex(element => element.loadStoreTarget == attachment.loadStoreTarget);
        if (index == -1)
        {
            index = colorAttachments.Count;
            colorAttachments.Add(attachment);
        }

        outputs.Add(index);
    }

    public void ReadColor(AttachmentDescriptor attachment)
    {
        var index = colorAttachments.FindIndex(element => element.loadStoreTarget == attachment.loadStoreTarget);
        if (index == -1)
        {
            index = colorAttachments.Count;
            colorAttachments.Add(attachment);
        }

        inputs.Add(index);
    }

    public void Reset()
    {
        size = 0;
        colorAttachments.Clear();
        depthAttachment = null;
        inputs.Clear();
        outputs.Clear();
    }

    /// <summary>
    /// Checks if this pass can merge with another pass, possibly as a subpass
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool CanMergeWithPass(NativeRenderPassData other)
    {
        // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
        if (size != other.size || depthAttachment.HasValue != other.depthAttachment.HasValue)
            return false;

        if (depthAttachment.HasValue && other.depthAttachment.HasValue && depthAttachment.Value.loadStoreTarget != other.depthAttachment.Value.loadStoreTarget)
            return false;

        return true;
    }

    public bool CanMergeWithSubPass(NativeRenderPassData other, int subPassIndex)
    {
        // A subpass can only merge with another sub pass if they have the exact same flags, color attachment count -and- output indices
        if (flags != other.subPasses[subPassIndex].flags || colorAttachments.Count != other.colorAttachments.Count)
            return false;

        for (var i = 0; i < colorAttachments.Count; i++)
        {
            if (colorAttachments[i].loadStoreTarget != other.colorAttachments[i].loadStoreTarget)
                return false;
        }

        return true;
    }

    public bool CanMergeWithPassAndSubPass(NativeRenderPassData other, int subPassIndex)
    {
        return CanMergeWithPass(other) && CanMergeWithSubPass(other, subPassIndex);
    }

    public void CopyTo(NativeRenderPassData other)
    {
        other.size = size;
        other.depthAttachment = depthAttachment;

        other.colorAttachments.Clear();
        other.colorAttachments.AddRange(colorAttachments);

        other.subPasses.Clear();

        var newSubPass = SubPassData.Create();
        foreach (var input in inputs)
            newSubPass.AddInput(input);

        foreach (var output in outputs)
            newSubPass.AddOutput(output);

        other.subPasses.Add(newSubPass);
    }
}
