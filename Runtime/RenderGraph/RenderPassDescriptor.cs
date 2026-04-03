using System;
using System.Text;
using Unity.Collections;
using UnityEngine.Rendering;

public readonly struct RenderPassDescriptor
{
    readonly Int2 size;
    readonly int viewCount, samples, depthAttachmentIndex, shadingRateImageAttachmentIndex;
    readonly NativeArray<AttachmentDescriptor> attachments;
    readonly NativeArray<SubPassDescriptor> subpasses;
    readonly string debugNameUtf8;

    public RenderPassDescriptor(Int2 size, NativeArray<AttachmentDescriptor> attachments, NativeArray<SubPassDescriptor> subpasses, int viewCount = 1, int samples = 1, int depthAttachmentIndex = -1, int shadingRateImageAttachmentIndex = -1, string debugNameUtf8 = default)
    {
        this.size = size;
        this.viewCount = viewCount;
        this.samples = samples;
        this.depthAttachmentIndex = depthAttachmentIndex;
        this.shadingRateImageAttachmentIndex = shadingRateImageAttachmentIndex;
        this.attachments = attachments;
        this.subpasses = subpasses;
        this.debugNameUtf8 = debugNameUtf8 ?? throw new ArgumentNullException(nameof(debugNameUtf8));
    }

    public readonly void BeginRenderPass(CommandBuffer command)
    {
        Span<byte> buffer = stackalloc byte[Encoding.UTF8.GetByteCount(debugNameUtf8)];
        _ = Encoding.UTF8.GetBytes(debugNameUtf8, buffer);
        command.BeginRenderPass(size.x, size.y, viewCount, samples, attachments, depthAttachmentIndex, shadingRateImageAttachmentIndex, subpasses, buffer);
    }
}