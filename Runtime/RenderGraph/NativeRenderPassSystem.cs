using System.Text;
using Unity.Collections;
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

        for (var i = 0; i < currentPassData.colorAttachments.Count; i++)
            attachments[i] = currentPassData.colorAttachments[i];

        if (hasDepth)
            attachments[attachmentCount - 1] = currentPassData.depthAttachment.Value;

        var subPasses = new NativeArray<SubPassDescriptor>(currentPassData.subPasses.Count, Allocator.Temp);

        for (var i = 0; i < currentPassData.subPasses.Count; i++)
            subPasses[i] = currentPassData.subPasses[i].Descriptor;

        var depthIndex = currentPassData.depthAttachment.HasValue ? attachmentCount - 1 : -1;
        var debugName = Encoding.UTF8.GetBytes(name);
        command.BeginRenderPass(currentPassData.size.x, currentPassData.size.y, currentPassData.size.z, 1, attachments, depthIndex, subPasses, debugName);
    }
}
