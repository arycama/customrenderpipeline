using Unity.Collections;
using UnityEngine.Rendering;

public readonly struct RenderPassDescriptor
{
    public readonly Int2 size;
    public readonly int viewCount, samples, depthAttachmentIndex, shadingRateImageAttachmentIndex;
    public readonly NativeArray<AttachmentData> attachments;
    public readonly NativeArray<SubPassDescriptor> subpasses;
    public readonly int startPassIndex, endPassIndex;
    public readonly string debugName;

    public RenderPassDescriptor(Int2 size, NativeArray<AttachmentData> attachments, NativeArray<SubPassDescriptor> subpasses, int startPassIndex, int endPassIndex, int viewCount = 1, int samples = 1, int depthAttachmentIndex = -1, int shadingRateImageAttachmentIndex = -1, string debugName = default)
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
        this.debugName = debugName;
    }

    public override string ToString() => $"{debugName} (size: {size}x{viewCount}, samples: {samples}, attachmentCount: {attachments.Length}, subpassCount: {subpasses.Length})";
}