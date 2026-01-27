using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Rendering;

public class NativeRenderPassSystem : IDisposable
{
    private Int3 size;
    private AttachmentDescriptor? depthAttachment;
    private bool isInRenderPass;
    private string passName;

    private NativeList<int> inputs = new(8, Allocator.Persistent), outputs = new(8, Allocator.Persistent);
    private SubPassFlags flags;

    private readonly NativeList<AttachmentDescriptor> colorAttachments = new(8, Allocator.Persistent);
    private readonly NativeList<SubPassDescriptor> subPasses = new(Allocator.Persistent);
    private readonly List<RenderPassDescriptor> renderPassDescriptors = new();

    public void Dispose()
    {
        inputs.Dispose();
        outputs.Dispose();
    }

    public void BeginNativeRenderPass(int index, CommandBuffer command)
    {
        renderPassDescriptors[index].BeginRenderPass(command);
    }

    private void BeginRenderPass(RenderPass pass)
    {
        isInRenderPass = true;

        pass.IsRenderPassStart = true;
        pass.RenderPassIndex = renderPassDescriptors.Count;

        passName = pass.Name;
        size = pass.size;
        depthAttachment = pass.depthAttachment;
        flags = pass.flags;
        colorAttachments.CopyFrom(pass.colorAttachments);

        for(var i = 0; i < pass.colorAttachments.Length; i++)
            outputs.Add(i);

        // TODO: Also write inputs
    }

    private void EndRenderPass(RenderPass pass)
    {
        EndSubPass();

        pass.IsRenderPassEnd = true;
        isInRenderPass = false;

        var depthIndex = -1;
        if (depthAttachment.HasValue)
        {
            depthIndex = colorAttachments.Length;
            colorAttachments.Add(depthAttachment.Value);
        }

        renderPassDescriptors.Add(new(size.x, size.y, new(colorAttachments.AsArray(), Allocator.Temp), new(subPasses.AsArray(), Allocator.Temp), size.z, 1, depthIndex, -1, passName));

        colorAttachments.Clear();
        subPasses.Clear();
        depthAttachment = null;
        passName = null;
    }

    private void BeginSubpass(RenderPass pass)
    {
        // Create new subpass, insert next subpass instruction
        pass.IsNextSubPass = true;

        foreach (var attachment in pass.colorAttachments)
        {
            var index = -1;
            for (var i = 0; i < colorAttachments.Length; i++)
            {
                if (colorAttachments[i].loadStoreTarget != attachment.loadStoreTarget)
                    continue;

                index = i;
                break;
            }

            if (index == -1)
            {
                index = colorAttachments.Length;
                colorAttachments.Add(attachment);
            }

            outputs.Add(index);
        }
    }

    private void EndSubPass()
    {
        subPasses.Add(new()
        {
            colorOutputs = new AttachmentIndexArray(outputs.AsArray()),
            inputs = new AttachmentIndexArray(inputs.AsArray()),
            flags = flags
        });

        outputs.Clear();
        inputs.Clear();
        flags = SubPassFlags.None;
    }

    public void CreateNativeRenderPasses(List<RenderPass> renderPasses)
    {
        foreach (var renderPass in renderPasses)
            renderPass.SetupRenderPassData();

        renderPassDescriptors.Clear();

        RenderPass previousPass = null;
        foreach (var pass in renderPasses)
        {
            var canMergePass = false;
            if (pass.IsNativeRenderPass)
            {
                // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
                canMergePass = isInRenderPass && size == pass.size && depthAttachment.HasValue == pass.depthAttachment.HasValue && (!depthAttachment.HasValue || !pass.depthAttachment.HasValue || depthAttachment.Value.loadStoreTarget == pass.depthAttachment.Value.loadStoreTarget);

                if (canMergePass)
                {
                    // If flags and attachements are identical, keep using the same subpass
                    var canMergeSubPass = flags == pass.flags && colorAttachments.Length == pass.colorAttachments.Length;
                    if (canMergeSubPass)
                    {
                        for (var i = 0; i < colorAttachments.Length; i++)
                        {
                            if (colorAttachments[i].loadStoreTarget == pass.colorAttachments[i].loadStoreTarget)
                                continue;

                            canMergeSubPass = false;
                            break;
                        }
                    }

                    if (!canMergeSubPass)
                    {
                        if (pass.AllowNewSubPass)
                        {
                            EndSubPass();
                            BeginSubpass(pass);
                        }
                        else
                        {
                            canMergePass = false;
                        }
                    }
                }
            }

            if(!canMergePass)
            {
                if (isInRenderPass)
                    EndRenderPass(previousPass);

                if(pass.IsNativeRenderPass)
                    BeginRenderPass(pass);
            }

            previousPass = pass;
        }

        // The frame may end on a final renderpass, in which case we need to end it
        if (isInRenderPass)
            EndRenderPass(previousPass);
    }
}