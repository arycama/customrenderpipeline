using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Rendering;

public class NativeRenderPassSystem
{
    private Int3 size;
    private AttachmentDescriptor? depthAttachment;

    private readonly List<AttachmentDescriptor> colorAttachments = new();
    private readonly List<SubPassData> subPasses = new();
    private readonly List<RenderPassDescriptor> renderPassDescriptors = new();

    public int RenderPassCount => renderPassDescriptors.Count;

    public void BeginNativeRenderPass(int index, CommandBuffer command)
    {
        renderPassDescriptors[index].BeginRenderPass(command);
    }

    public void CreateNativeRenderPasses(List<RenderPass> renderPasses)
    {
        renderPassDescriptors.Clear();

        for (var i = 0; i < renderPasses.Count; i++)
        {
            var pass = renderPasses[i];
            if (!pass.IsNativeRenderPass)
                continue;

            // Detect start or next subpass conditions
            // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
            var canMergeWithCurrentPass = size == pass.size && depthAttachment.HasValue == pass.depthAttachment.HasValue && (!depthAttachment.HasValue || !pass.depthAttachment.HasValue || depthAttachment.Value.loadStoreTarget == pass.depthAttachment.Value.loadStoreTarget);

            var canMergeWithSubPass = subPasses.Count > 0;
            if (canMergeWithSubPass)
            {
                if (pass.flags != subPasses[^1].flags || colorAttachments.Count != pass.colorAttachments.Count)
                    canMergeWithSubPass = false;

                if (canMergeWithSubPass)
                {
                    for (var j = 0; j < colorAttachments.Count; j++)
                    {
                        if (colorAttachments[j].loadStoreTarget != pass.colorAttachments[j].loadStoreTarget)
                            canMergeWithSubPass = false;
                    }
                }
            }

            if (!canMergeWithSubPass && canMergeWithCurrentPass && pass.AllowNewSubPass)
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

            if (canMergeWithCurrentPass && !canMergeWithSubPass && !pass.AllowNewSubPass || !canMergeWithCurrentPass)
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

            // Detect end conditions
            var isLastPass = i == renderPasses.Count - 1;
            var nextPass = isLastPass ? null : renderPasses[i + 1];
            var isNextPassNativeRenderPass = nextPass != null && nextPass.IsNativeRenderPass;

            // If we can start a new subpass, we only need to end if the pass is not compatible (Assume we can always make new subpasses for now)
            var canMergeWithNextPass = isNextPassNativeRenderPass;

            // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
            if (size != nextPass.size || depthAttachment.HasValue != nextPass.depthAttachment.HasValue)
                canMergeWithNextPass = false;

            if (depthAttachment.HasValue && nextPass.depthAttachment.HasValue && depthAttachment.Value.loadStoreTarget != nextPass.depthAttachment.Value.loadStoreTarget)
                canMergeWithNextPass = false;

            if (!nextPass.AllowNewSubPass)
            {
                // If subpass creation is not allowed, we can only merge if both the pass and subpass match.
                // A subpass can only merge with another sub pass if they have the exact same flags, color attachment count -and- output indices
                if (nextPass.flags != subPasses[0].flags || colorAttachments.Count != nextPass.colorAttachments.Count)
                    canMergeWithNextPass = false;

                if (canMergeWithNextPass)
                {
                    for (var j = 0; j < colorAttachments.Count; j++)
                    {
                        if (colorAttachments[j].loadStoreTarget != nextPass.colorAttachments[j].loadStoreTarget)
                            canMergeWithNextPass = false;
                    }
                }
            }

            // End renderpass if this is the last pass, or the next pass is not a renderpass, or the next can not be merged with the current
            if (isLastPass || !isNextPassNativeRenderPass || !canMergeWithNextPass)
            {
                pass.IsRenderPassEnd = true;

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

                if (isLastPass || !isNextPassNativeRenderPass)
                {
                    colorAttachments.Clear();
                    subPasses.Clear();
                    size = 0;
                    depthAttachment = null;
                }
            }
        }
    }
}