using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Rendering;

public class NativeRenderPassSystem
{
    private Int3 size;
    private AttachmentDescriptor? depthAttachment;
    private bool isInRenderPass;

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
        // Can't make a new subpass, need to end previous renderpass and start a new one
        pass.IsRenderPassStart = true;

        pass.RenderPassIndex = RenderPassCount;
        colorAttachments.Clear();
        subPasses.Clear();
        size = pass.size;
        depthAttachment = pass.depthAttachment;
        subPasses.Add(SubPassData.Create());

        foreach (var attachment in pass.colorAttachments)
        {
            subPasses[0].AddOutput(colorAttachments.Count);
            colorAttachments.Add(attachment);
        }

        // TODO: Also write inputs
    }

    private void BeginSubpass(RenderPass pass)
    {
        // Create new subpass, insert next subpass instruction
        pass.IsNextSubPass = true;
        subPasses.Add(SubPassData.Create());

        foreach (var attachment in pass.colorAttachments)
        {
            var index = colorAttachments.FindIndex(element => element.loadStoreTarget == attachment.loadStoreTarget);
            if (index == -1)
            {
                index = colorAttachments.Count;
                colorAttachments.Add(attachment);
            }

            subPasses[^1].AddOutput(index);
        }
    }

    private void EndRenderPass(RenderPass pass)
    {

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
        renderPassDescriptors.Add(new(size.x, size.y, attachments, subPassesResult, size.z, 1, depthIndex, -1, pass.Name));

        colorAttachments.Clear();
        subPasses.Clear();
        size = 0;
        depthAttachment = null;
    }

    public void CreateNativeRenderPasses(List<RenderPass> renderPasses)
    {
        foreach (var renderPass in renderPasses)
        {
            renderPass.SetupRenderPassData();
        }

        renderPassDescriptors.Clear();

        for (var i = 0; i < renderPasses.Count; i++)
        {
            var pass = renderPasses[i];
            if (!pass.IsNativeRenderPass)
                continue;

            // Detect start or next subpass conditions
            // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
            var canMergeWithPass = isInRenderPass &&
                size == pass.size && 
                depthAttachment.HasValue == pass.depthAttachment.HasValue &&
                (!depthAttachment.HasValue || !pass.depthAttachment.HasValue || depthAttachment.Value.loadStoreTarget == pass.depthAttachment.Value.loadStoreTarget);

            if(canMergeWithPass)
            {
                // If flags and attachements are identical, keep using the same subpass
                var canMergeWithSubPass = pass.flags == subPasses[^1].flags && colorAttachments.Count == pass.colorAttachments.Count;
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
                    if(pass.AllowNewSubPass)
                    {
                        BeginSubpass(pass);
                    }
                    else
                    {
                        // Previous pass will have already ended, so need to re-end.
                        BeginRenderPass(pass);
                        isInRenderPass = true;
                    }
                }
            }
            else
            {
                // Previous pass will have already ended, so need to re-end.
                BeginRenderPass(pass);
                isInRenderPass = true;
            }

            // Detect end conditions
            var isLastPass = i == renderPasses.Count - 1;
            var nextPass = isLastPass ? null : renderPasses[i + 1];
            var isNextPassNativeRenderPass = nextPass != null && nextPass.IsNativeRenderPass;

            // If we can start a new subpass, we only need to end if the pass is not compatible (Assume we can always make new subpasses for now)
            // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
            var canMergeWithNextPass = isNextPassNativeRenderPass && size == nextPass.size && depthAttachment.HasValue == nextPass.depthAttachment.HasValue && (!depthAttachment.HasValue || !nextPass.depthAttachment.HasValue || depthAttachment.Value.loadStoreTarget == nextPass.depthAttachment.Value.loadStoreTarget);

            if (canMergeWithNextPass)
            {
                var canMergeWithNextSubPass = nextPass.flags == subPasses[^1].flags && colorAttachments.Count == nextPass.colorAttachments.Count;
                if (canMergeWithNextSubPass)
                    for (var j = 0; j < colorAttachments.Count; j++)
                        if (colorAttachments[j].loadStoreTarget != nextPass.colorAttachments[j].loadStoreTarget)
                        {
                            canMergeWithNextSubPass = false;
                            break;
                        }

                if(!canMergeWithNextSubPass)
                {
                    if(nextPass.AllowNewSubPass)
                    {
                        // New subpass will be created, no need to call end
                    }
                    else
                    {
                        // Need to start a new render pass
                        pass.IsRenderPassEnd = true;
                        EndRenderPass(pass);
                    }
                }
            }
            else
            {
                // Need to start a new render pass
                pass.IsRenderPassEnd = true;
                EndRenderPass(pass);
            }
        }
    }
}