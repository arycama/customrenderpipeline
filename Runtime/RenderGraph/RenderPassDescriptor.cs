using System;
using System.Text;
using Unity.Collections;
using UnityEngine.Rendering;

public readonly struct RenderPassDescriptor
{
    readonly int width, height, depth, samples, depthAttachmentIndex, shadingRateImageAttachmentIndex;
    readonly NativeArray<AttachmentDescriptor> attachments;
    readonly NativeArray<SubPassDescriptor> subpasses;
    readonly string debugNameUtf8;

    public RenderPassDescriptor(int width, int height, NativeArray<AttachmentDescriptor> attachments, NativeArray<SubPassDescriptor> subpasses, int depth = 1, int samples = 1, int depthAttachmentIndex = -1, int shadingRateImageAttachmentIndex = -1, string debugNameUtf8 = default)
    {
        this.width = width;
        this.height = height;
        this.depth = depth;
        this.samples = samples;
        this.depthAttachmentIndex = depthAttachmentIndex;
        this.shadingRateImageAttachmentIndex = shadingRateImageAttachmentIndex;
        this.attachments = attachments;
        this.subpasses = subpasses;
        this.debugNameUtf8 = debugNameUtf8 ?? throw new ArgumentNullException(nameof(debugNameUtf8));
    }

    public readonly void BeginRenderPass(CommandBuffer command) => command.BeginRenderPass(width, height, depth, samples, attachments, depthAttachmentIndex, shadingRateImageAttachmentIndex, subpasses, Encoding.UTF8.GetBytes(debugNameUtf8));
}