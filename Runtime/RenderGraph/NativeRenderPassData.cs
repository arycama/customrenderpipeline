using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class NativeRenderPassData
{
    public Int3 size;
    public AttachmentDescriptor? depthAttachment;
    public readonly List<AttachmentDescriptor> colorAttachments = new();
    public readonly List<SubPassData> subPasses = new();

    public RenderPassDescriptor GetDescriptor(string name)
    {
        var attachmentCount = colorAttachments.Count;
        var hasDepth = depthAttachment.HasValue;
        if (hasDepth)
            attachmentCount++;

        var attachments = new NativeArray<AttachmentDescriptor>(attachmentCount, Allocator.Temp);

        for (var i = 0; i < colorAttachments.Count; i++)
            attachments[i] = colorAttachments[i];

        if (hasDepth)
            attachments[attachmentCount - 1] = depthAttachment.Value;

        var subPasses = new NativeArray<SubPassDescriptor>(this.subPasses.Count, Allocator.Temp);

       for (var i = 0; i < this.subPasses.Count; i++)
            subPasses[i] = this.subPasses[i].Descriptor;

        var depthIndex = depthAttachment.HasValue ? attachmentCount - 1 : -1;
        return new RenderPassDescriptor(size.x, size.y, attachments, subPasses, size.z, 1, depthIndex, -1, name);
    }

    public void SetSize(Int3 size)
    {
        this.size = size;
    }

    public void SetSubPassFlags(int subPassIndex, SubPassFlags flags)
    {
        var subPass = subPasses[subPassIndex];
        subPass.flags = flags;
        subPasses[subPassIndex] = subPass;
    }

    public void WriteDepth(int subPass, GraphicsFormat format, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, RenderTargetIdentifier loadStoreTarget)
    {
        depthAttachment = new AttachmentDescriptor(format) { loadAction = loadAction, storeAction = storeAction, loadStoreTarget = loadStoreTarget };

        while (subPasses.Count <= subPass)
            subPasses.Add(SubPassData.Create());
    }

    public void AddColorOutput(int subPass, GraphicsFormat format, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, RenderTargetIdentifier loadStoreTarget, Color clearColor = default)
    {
        var index = colorAttachments.FindIndex(element => element.loadStoreTarget == loadStoreTarget);

        if (index == -1)
        {
            index = colorAttachments.Count;
            var attachment = new AttachmentDescriptor(format) { loadAction = loadAction, storeAction = storeAction, loadStoreTarget = loadStoreTarget, clearColor = clearColor };
            colorAttachments.Add(attachment);
        }

        while (subPasses.Count <= subPass)
            subPasses.Add(SubPassData.Create());

        // TODO: Are we able to directly assign instead of this
        var subpassOutput = subPasses[subPass];
        subpassOutput.AddOutput(index);
        subPasses[subPass] = subpassOutput;
    }

    public void Reset()
    {
        size = 0;
        colorAttachments.Clear();
        depthAttachment = null;
        subPasses.Clear();
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
        var subPass = subPasses[0];

        // A subpass can only merge with another sub pass if they have the exact same flags, color attachment count -and- output indices
        if (subPass.flags != other.subPasses[subPassIndex].flags || colorAttachments.Count != other.colorAttachments.Count)
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
}

public struct SubPassData
{
    public NativeList<int> inputs, outputs;
    public SubPassFlags flags;

    public static SubPassData Create()
    {
        return new SubPassData { inputs = new NativeList<int>(8, Allocator.Temp), outputs = new NativeList<int>(8, Allocator.Temp) };
    }

    public SubPassDescriptor Descriptor => new()
    {
        flags = flags,
        colorOutputs = new(outputs.AsArray()),
        inputs = new(inputs.AsArray())
    };

    public void AddInput(int index)
    {
        inputs.Add(index);
    }

    public void AddOutput(int index)
    {
        outputs.Add(index);
    }

    public void Clear()
    {
        inputs.Clear();
        outputs.Clear();
        flags = SubPassFlags.None;
    }
}