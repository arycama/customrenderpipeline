using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class NativeRenderPassSystem
{
    public void BeginRenderPass(CommandBuffer command, NativeRenderSubPassData currentPassData)
    {
        var attachmentCount = currentPassData.colorAttachments.Count;
        if (currentPassData.depthAttachment.HasValue)
            attachmentCount++;

        var attachments = new NativeArray<AttachmentDescriptor>(attachmentCount, Allocator.Temp);

        if (currentPassData.depthAttachment.HasValue)
            attachments[0] = currentPassData.depthAttachment.Value;

        for (var i = 0; i < currentPassData.colorAttachments.Count; i++)
        {
            var index = i;
            if (currentPassData.depthAttachment.HasValue)
                index++;

            attachments[index] = currentPassData.colorAttachments[i];
        }

        var colorOutputs = new AttachmentIndexArray(currentPassData.subPassOutputs.Count);
        for (var i = 0; i < currentPassData.subPassOutputs.Count; i++)
        {
            var output = currentPassData.subPassOutputs[i];

            // We store depth as the first attachment if available, so need to increment all the indices by 1 in this case
            if (currentPassData.depthAttachment.HasValue)
                output += 1;

            colorOutputs[i] = output;
        }

        var subPasses = new NativeArray<SubPassDescriptor>(1, Allocator.Temp);
        {
            subPasses[0] = new SubPassDescriptor() { colorOutputs = colorOutputs, flags = currentPassData.flags };
        }

        
        var depthIndex = currentPassData.depthAttachment.HasValue ? 0 : -1;
        command.BeginRenderPass(currentPassData.size.x, currentPassData.size.y, currentPassData.size.z, 1, attachments, depthIndex, subPasses);
    }
}
