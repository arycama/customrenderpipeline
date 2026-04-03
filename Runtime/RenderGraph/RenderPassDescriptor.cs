using System;
using System.Text;
using Unity.Collections;
using UnityEngine.Rendering;

public readonly struct RenderPassDescriptor
{
    readonly Int2 size;
    readonly int viewCount, samples, depthAttachmentIndex, shadingRateImageAttachmentIndex;
    readonly NativeArray<AttachmentData> attachments;
    readonly NativeArray<SubPassDescriptor> subpasses;
    readonly int startPassIndex, endPassIndex;
    readonly string debugNameUtf8;

    public RenderPassDescriptor(Int2 size, NativeArray<AttachmentData> attachments, NativeArray<SubPassDescriptor> subpasses, int startPassIndex, int endPassIndex, int viewCount = 1, int samples = 1, int depthAttachmentIndex = -1, int shadingRateImageAttachmentIndex = -1, string debugNameUtf8 = default)
    {
        this.size = size;
        this.viewCount = viewCount;
        this.samples = samples;
        this.depthAttachmentIndex = depthAttachmentIndex;
        this.shadingRateImageAttachmentIndex = shadingRateImageAttachmentIndex;
        this.attachments = attachments;
        this.subpasses = subpasses;
        this.startPassIndex = startPassIndex;
        this.endPassIndex = endPassIndex;
        this.debugNameUtf8 = debugNameUtf8 ?? throw new ArgumentNullException(nameof(debugNameUtf8));
    }

    public readonly void BeginRenderPass(CommandBuffer command, RenderGraph renderGraph)
    {
        var attachments = new NativeArray<AttachmentDescriptor>(this.attachments.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        for (var i = 0; i < this.attachments.Length; i++)
        {
            var colorAttachment = this.attachments[i];
            if (colorAttachment.isFrameBufferOutput)
            {
                // TODO: what happens if we use don't care for frameBuffer output, does it still get stored (possibly in an optimal way?)
                attachments[i] = new(colorAttachment.frameBufferFormat)
                {
                    //storeAction = RenderBufferStoreAction.Store,
                    loadStoreTarget = colorAttachment.frameBufferTarget
                };
            }
            else
            {
                var handleData = renderGraph.RtHandleSystem.GetHandleData(colorAttachment.handle);
                var attachment = new AttachmentDescriptor(handleData.descriptor.format);

                // If the handle was created before this native render pass started, we need to load the contents. Otherwise it can be cleared or discarded
                var requiresLoad = handleData.createIndex1 < startPassIndex || handleData.createIndex1 == -1;
                if (requiresLoad)
                    attachment.loadAction = RenderBufferLoadAction.Load;
                else if (handleData.descriptor.clear)
                {
                    attachment.loadAction = RenderBufferLoadAction.Clear;
                    attachment.clearColor = handleData.descriptor.clearColor;
                }

                // If the handle gets freed before this native render pass ends, we can discard the contents, otherwise they must be stored as another pass is going to use it
                var requiresStore = handleData.freeIndex1 > endPassIndex || handleData.freeIndex1 == -1;
                if (requiresStore)
                    attachment.storeAction = RenderBufferStoreAction.Store;

                // If the handle is created and freed during the renderpass, we can avoid allocating a target entirely. (TODO: The render target system may still create a texture which may be unused)
                if (requiresLoad || requiresStore)
                {
                    if (colorAttachment.isFrameBufferOutput)
                        attachment.loadStoreTarget = colorAttachment.frameBufferTarget;
                    else
                    {
                        var target = renderGraph.RtHandleSystem.GetResource(colorAttachment.handle);
                        attachment.loadStoreTarget = new(target, colorAttachment.mipLevel, colorAttachment.cubemapFace, colorAttachment.depthSlice);
                    }
                }

                attachments[i] = attachment;
            }
        }

        Span<byte> buffer = stackalloc byte[Encoding.UTF8.GetByteCount(debugNameUtf8)];
        _ = Encoding.UTF8.GetBytes(debugNameUtf8, buffer);
        command.BeginRenderPass(size.x, size.y, viewCount, samples, attachments, depthAttachmentIndex, shadingRateImageAttachmentIndex, subpasses, buffer);
    }
}