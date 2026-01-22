using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class NativeRenderPassSystem
{
    private bool isInNativeRenderPass;

    public void BeginRenderPass(CommandBuffer command, NativeRenderPassData currentPassData)
    {
        //Assert.IsFalse(isInNativeRenderPass);

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

        if (isInNativeRenderPass)
            Debug.LogError("Starting a render pass without ending one");

        command.BeginRenderPass(currentPassData.size.x, currentPassData.size.y, currentPassData.size.z, 1, attachments, depthIndex, subPasses);
        isInNativeRenderPass = true;
    }

    public void EndRenderPass(CommandBuffer command)
    {
        // Assert.IsTrue(isInNativeRenderPass);

        if (!isInNativeRenderPass)
            Debug.LogError("Ending a render pass without being in one");

        command.EndRenderPass();
        isInNativeRenderPass = false;
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

    /// <summary>
    /// Checks if this pass can merge with another pass, possibly as a subpass
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool CanMergeWithPass(NativeRenderPassData other)
    {
        return size == other.size &&
            hasDepthAttachment == other.hasDepthAttachment &&
            flags == other.flags &&
            depthAttachment.loadStoreTarget == other.depthAttachment.loadStoreTarget;
    }

    public bool CanMergeWithSubPass(NativeRenderPassData other)
    {
        if (colorAttachments.Count != other.colorAttachments.Count)
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