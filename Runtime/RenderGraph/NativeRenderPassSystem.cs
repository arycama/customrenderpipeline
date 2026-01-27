using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Rendering;

public class NativeRenderPassSystem
{
    private Int3 size;
    private AttachmentDescriptor? depthAttachment;
    private bool isInRenderPass;
    private string passName;

    private SubPassData currentSubPass;

    private readonly List<AttachmentDescriptor> colorAttachments = new();
    private readonly List<SubPassData> subPasses = new();
    private readonly List<RenderPassDescriptor> renderPassDescriptors = new();

    public int RenderPassCount => renderPassDescriptors.Count;

    public void BeginNativeRenderPass(int index, CommandBuffer command)
    {
        renderPassDescriptors[index].BeginRenderPass(command);
    }

    private void BeginRenderPass(RenderPass pass)
    {
        passName = pass.Name;

        // Can't make a new subpass, need to end previous renderpass and start a new one
        pass.IsRenderPassStart = true;
        isInRenderPass = true;

        pass.RenderPassIndex = RenderPassCount;
        size = pass.size;
        depthAttachment = pass.depthAttachment;

        currentSubPass = new SubPassData(new NativeList<int>(8, Allocator.Temp), new NativeList<int>(8, Allocator.Temp), pass.flags);

        foreach (var attachment in pass.colorAttachments)
        {
            currentSubPass.AddOutput(colorAttachments.Count);
            colorAttachments.Add(attachment);
        }

        // TODO: Also write inputs
    }

    private void EndRenderPass(RenderPass pass)
    {
        EndSubPass(currentSubPass);

        pass.IsRenderPassEnd = true;
        isInRenderPass = false;

        // Add descriptor to list
        var attachmentCount = colorAttachments.Count;
        var hasDepth = depthAttachment.HasValue;
        if (hasDepth)
            attachmentCount++;

        var attachments = new NativeArray<AttachmentDescriptor>(attachmentCount, Allocator.Temp);

        for (var j = 0; j < colorAttachments.Count; j++)
            attachments[j] = colorAttachments[j];

        if (hasDepth)
            attachments[attachmentCount - 1] = depthAttachment.Value;

        var subPassesResult = new NativeArray<SubPassDescriptor>(subPasses.Count, Allocator.Temp);

        for (var j = 0; j < subPasses.Count; j++)
            subPassesResult[j] = subPasses[j].Descriptor;

        var depthIndex = depthAttachment.HasValue ? attachmentCount - 1 : -1;
        renderPassDescriptors.Add(new(size.x, size.y, attachments, subPassesResult, size.z, 1, depthIndex, -1, passName));

        colorAttachments.Clear();
        subPasses.Clear();
        size = 0;
        depthAttachment = null;
        passName = null;
    }

    private void BeginSubpass(RenderPass pass)
    {
        // Create new subpass, insert next subpass instruction
        pass.IsNextSubPass = true;
        currentSubPass = new SubPassData(new NativeList<int>(8, Allocator.Temp), new NativeList<int>(8, Allocator.Temp), pass.flags);

        foreach (var attachment in pass.colorAttachments)
        {
            var index = colorAttachments.FindIndex(element => element.loadStoreTarget == attachment.loadStoreTarget);
            if (index == -1)
            {
                index = colorAttachments.Count;
                colorAttachments.Add(attachment);
            }

            currentSubPass.AddOutput(index);
        }
    }

    private void EndSubPass(SubPassData subPass)
    {
        subPasses.Add(subPass);
    }

    public void CreateNativeRenderPasses(List<RenderPass> renderPasses)
    {
        foreach (var renderPass in renderPasses)
            renderPass.SetupRenderPassData();

        renderPassDescriptors.Clear();

        RenderPass previousPass = null;
        for (var i = 0; i < renderPasses.Count; i++)
        {
            var pass = renderPasses[i];
            if (pass.IsNativeRenderPass)
            {
                // Detect start or next subpass conditions
                // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
                var canMergeWithPass = isInRenderPass &&
                    size == pass.size &&
                    depthAttachment.HasValue == pass.depthAttachment.HasValue &&
                    (!depthAttachment.HasValue || !pass.depthAttachment.HasValue || depthAttachment.Value.loadStoreTarget == pass.depthAttachment.Value.loadStoreTarget);

                if (canMergeWithPass)
                {
                    // If flags and attachements are identical, keep using the same subpass
                    var canMergeWithSubPass = pass.flags == currentSubPass.flags && colorAttachments.Count == pass.colorAttachments.Count;
                    if (canMergeWithSubPass)
                        for (var j = 0; j < colorAttachments.Count; j++)
                            if (colorAttachments[j].loadStoreTarget != pass.colorAttachments[j].loadStoreTarget)
                            {
                                canMergeWithSubPass = false;
                                break;
                            }

                    // Otherwise we need to start a new subpass if possible, otherwise a new render pass.
                    if (!canMergeWithSubPass)
                    {
                        if (pass.AllowNewSubPass)
                        {
                            EndSubPass(currentSubPass);
                            BeginSubpass(pass);
                        }
                        else
                        {
                            if (previousPass != null && previousPass.IsNativeRenderPass)
                                EndRenderPass(previousPass);
                            BeginRenderPass(pass);
                        }
                    }
                }
                else
                {
                    if (previousPass != null && previousPass.IsNativeRenderPass)
                        EndRenderPass(previousPass);
                    BeginRenderPass(pass);
                }
            }
            else if (previousPass != null && previousPass.IsNativeRenderPass)
                EndRenderPass(previousPass);

            previousPass = pass;
        }

        if (previousPass != null && previousPass.IsNativeRenderPass)
            EndRenderPass(previousPass);
    }
}