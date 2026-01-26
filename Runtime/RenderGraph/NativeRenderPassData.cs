using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class NativeRenderPassData
{
    public Int3 size;
    public AttachmentDescriptor? depthAttachment;
    public SubPassFlags flags;
    public readonly List<AttachmentDescriptor> colorAttachments = new();
    public readonly List<SubPassData> subPasses = new();

    public void SetSize(Int3 size)
    {
        this.size = size;
    }

    public void SetSubPassFlags(SubPassFlags flags)
    {
        this.flags = flags;
    }

    public void WriteDepth(int subPass, GraphicsFormat format, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, RenderTargetIdentifier loadStoreTarget)
    {
        depthAttachment = new AttachmentDescriptor(format) { loadAction = loadAction, storeAction = storeAction, loadStoreTarget = loadStoreTarget };

        while (subPasses.Count <= subPass)
            subPasses.Add(new SubPassData());
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
            subPasses.Add(new SubPassData());

        // TODO: Are we able to directly assign instead of this
        var subpassOutput = subPasses[subPass];
        subpassOutput.AddAttachment(index);
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
        return size == other.size && depthAttachment == other.depthAttachment;
    }

    public bool CanMergeWithSubPass(NativeRenderPassData other)
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

    public bool CanMergeWithPassAndSubPass(NativeRenderPassData other)
    {
        return CanMergeWithPass(other) && CanMergeWithSubPass(other);
    }
}

public struct SubPassData
{
    private int a0, a1, a2, a3, a4, a5, a6, a7;
    public int Count { get; private set; }

    public int this[int index]
    {
        readonly get
        {
            Assert.IsTrue(index > -1 && index < Count);

            return index switch
            {
                0 => a0,
                1 => a1,
                2 => a2,
                3 => a3,
                4 => a4,
                5 => a5,
                6 => a6,
                7 => a7,
            };
        }

        set
        {
            Assert.IsTrue(index > -1 && index < 8);

            if (Count < index)
                Count = index;

            switch (index)
            {
                case 0:
                    a0 = index;
                    break;
                case 1:
                    a1 = index;
                    break;
                case 2:
                    a2 = index;
                    break;
                case 3:
                    a3 = index;
                    break;
                case 4:
                    a4 = index;
                    break;
                case 5:
                    a5 = index;
                    break;
                case 6:
                    a6 = index;
                    break;
                case 7:
                    a7 = index;
                    break;
            }
        }
    }

    public void AddAttachment(int index)
    {
        Assert.IsTrue(Count < 7);
        this[Count++] = index;
    }

    public void Clear()
    {
        Count = 0;
    }
}