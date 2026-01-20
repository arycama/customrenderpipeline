using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class NativeRenderPassSystem
{
    public bool IsInNativeRenderPass { get; private set; }

    private NativeRenderPassData previousPassData = new();

    public void BeginRenderPass(CommandBuffer command, NativeRenderPassData currentPassData)
    {
        var attachmentCount = currentPassData.colorAttachments.Count;
        if (currentPassData.hasDepthAttachment)
            attachmentCount++;

        var attachments = new NativeArray<AttachmentDescriptor>(attachmentCount, Allocator.Temp);
        var colorOutputs = new AttachmentIndexArray(currentPassData.colorAttachments.Count);

        if (currentPassData.hasDepthAttachment)
            attachments[0] = currentPassData.depthAttachment;

        for (var i = 0; i < currentPassData.colorAttachments.Count; i++)
        {
            var index = i;
            if (currentPassData.hasDepthAttachment)
                index++;

            attachments[index] = currentPassData.colorAttachments[i];
            colorOutputs[i] = index;
        }

        var depthIndex = currentPassData.hasDepthAttachment ? 0 : -1;
        var subPasses = new NativeArray<SubPassDescriptor>(1, Allocator.Temp);
        {
            subPasses[0] = new SubPassDescriptor() { colorOutputs = colorOutputs, flags = currentPassData.flags };
        }

        if (IsInNativeRenderPass)
        {
            if (!currentPassData.MatchesPass(previousPassData))
            {
                command.EndRenderPass();
                command.BeginRenderPass(currentPassData.size.x, currentPassData.size.y, currentPassData.size.z, 1, attachments, depthIndex, subPasses);

                // Swap current and previous
                (previousPassData, currentPassData) = (currentPassData, previousPassData);
            }
        }
        else
        {
            command.BeginRenderPass(currentPassData.size.x, currentPassData.size.y, currentPassData.size.z, 1, attachments, depthIndex, subPasses);

            // Swap current and previous
            (previousPassData, currentPassData) = (currentPassData, previousPassData);

            IsInNativeRenderPass = true;
        }

        // Reset
        currentPassData.Reset();
    }

    public void EndRenderPass(CommandBuffer command)
    {
        command.EndRenderPass();
        IsInNativeRenderPass = false;
    }
}

public class NativeRenderPassData
{
    public Int3 size;
    public bool hasDepthAttachment;
    public SubPassFlags flags;
    public AttachmentDescriptor depthAttachment;
    public readonly List<AttachmentDescriptor> colorAttachments = new();

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
        hasDepthAttachment = true;
    }

    public void AddAttachment(GraphicsFormat format, RenderBufferLoadAction loadAction, RenderBufferStoreAction storeAction, RenderTargetIdentifier loadStoreTarget, Color clearColor = default)
    {
        colorAttachments.Add(new AttachmentDescriptor(format) { loadAction = loadAction, storeAction = storeAction, loadStoreTarget = loadStoreTarget, clearColor = clearColor });
    }

    public void Reset()
    {
        size = 0;
        colorAttachments.Clear();
        depthAttachment = default;
        hasDepthAttachment = false;
    }

    public bool MatchesPass(NativeRenderPassData other)
    {
        var isEqual = size == other.size &&
            hasDepthAttachment == other.hasDepthAttachment &&
            flags == other.flags &&
            depthAttachment.loadStoreTarget == other.depthAttachment.loadStoreTarget &&
            colorAttachments.Count == other.colorAttachments.Count;

        if (!isEqual)
            return false;

        for (var i = 0; i < colorAttachments.Count; i++)
        {
            if (colorAttachments[i].loadStoreTarget != other.colorAttachments[i].loadStoreTarget)
                return false;
        }

        return true;
    }
}