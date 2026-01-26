using System;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class NativeRenderPassSystem
{
    public void BeginRenderPass(CommandBuffer command, NativeRenderPassData currentPassData, string name)
    {
        var attachmentCount = currentPassData.colorAttachments.Count;
        var hasDepth = currentPassData.depthAttachment.HasValue;
        if (hasDepth)
            attachmentCount++;

        var attachments = new NativeArray<AttachmentDescriptor>(attachmentCount, Allocator.Temp);

        if (hasDepth)
            attachments[0] = currentPassData.depthAttachment.Value;

        for (var i = 0; i < currentPassData.colorAttachments.Count; i++)
        {
            var index = i;
            if (hasDepth)
                index++;

            attachments[index] = currentPassData.colorAttachments[i];
        }

        Assert.IsFalse(currentPassData.subPasses.Count == 0);

        var subPasses = new NativeArray<SubPassDescriptor>(currentPassData.subPasses.Count, Allocator.Temp);

        for (var i = 0; i < currentPassData.subPasses.Count; i++)
        {
            var subpass = currentPassData.subPasses[i];
            var subpassOutputs = new AttachmentIndexArray(subpass.Count);

            for(var j = 0; j < subpass.Count; j++)
            {
                // We store depth as the first attachment if available, so need to increment all the indices by 1 in this case
                var value = subpass[j];
                Assert.IsFalse(value == -1);
                subpassOutputs[j] = value + (hasDepth ? 1 : 0);
            }

            subPasses[i] = new SubPassDescriptor() { colorOutputs = subpassOutputs, flags = currentPassData.flags };
        }
        
        var depthIndex = currentPassData.depthAttachment.HasValue ? 0 : -1;
        var debugName = Encoding.UTF8.GetBytes(name);
        command.BeginRenderPass(currentPassData.size.x, currentPassData.size.y, currentPassData.size.z, 1, attachments, depthIndex, subPasses, debugName);
    }
}
