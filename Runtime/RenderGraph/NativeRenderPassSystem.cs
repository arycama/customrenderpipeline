using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class NativeRenderPassSystem : IDisposable
{
    private readonly RenderGraph renderGraph;

    private Int3 size;
    private AttachmentData? depthAttachment;
    private bool isInRenderPass;
    private string passName;

    private NativeList<AttachmentData> inputs = new(8, Allocator.Persistent), outputs = new(8, Allocator.Persistent);
    private SubPassFlags flags;

    private int startPassIndex, endPassIndex;

    private readonly NativeList<AttachmentData> colorAttachments = new(8, Allocator.Persistent);
    private readonly NativeList<SubPassDescriptor> subPasses = new(Allocator.Persistent);
    private readonly List<RenderPassDescriptor> renderPassDescriptors = new();

    public NativeRenderPassSystem(RenderGraph renderGraph)
    {
        this.renderGraph = renderGraph;
    }

    public void Dispose()
    {
        inputs.Dispose();
        outputs.Dispose();
    }

    public void BeginNativeRenderPass(int index, CommandBuffer command)
    {
        renderPassDescriptors[index].BeginRenderPass(command);
    }

    private void BeginRenderPass(RenderPass pass)
    {
        isInRenderPass = true;

        startPassIndex = pass.Index;

        pass.IsRenderPassStart = true;
        pass.RenderPassIndex = renderPassDescriptors.Count;

        passName = pass.Name;
        flags = pass.flags;

        if (pass.OutputsToCameraTarget)
        {
            size = pass.FrameBufferSize;
        }
        else if (pass.depthBuffer.HasValue)
        {
            var target = renderGraph.RtHandleSystem.GetResource(pass.depthBuffer.Value);
            depthAttachment = new(pass.depthBuffer.Value, new(target, pass.MipLevel, pass.CubemapFace, pass.DepthSlice));
            size = new(target.width, target.height, target.volumeDepth);
        }
        else
        {
            var target = renderGraph.RtHandleSystem.GetResource(pass.colorTargets[0]);
            size = new(target.width, target.height, target.volumeDepth);
        }
    }

    private void BeginSubpass(RenderPass pass)
    {
        foreach (var input in pass.frameBufferInputs)
        {
            var target = renderGraph.RtHandleSystem.GetResource(input);
            inputs.Add(new(input, new(target, 0, CubemapFace.Unknown, -1)));
        }

        if (pass.OutputsToCameraTarget)
        {
            outputs.Add(new(default, pass.FrameBufferTarget, pass.FrameBufferFormat, true));
        }
        else
        {
            if (pass.depthBuffer.HasValue)
                depthAttachment = new(pass.depthBuffer.Value, new(renderGraph.RtHandleSystem.GetResource(pass.depthBuffer.Value), pass.MipLevel, pass.CubemapFace, pass.DepthSlice));

            foreach (var colorTarget in pass.colorTargets)
                outputs.Add(new(colorTarget, new(renderGraph.RtHandleSystem.GetResource(colorTarget), pass.MipLevel, pass.CubemapFace, pass.DepthSlice)));
        }
    }

    private void EndRenderPass(RenderPass pass)
    {
        EndSubPass();

        endPassIndex = pass.Index;

        pass.IsRenderPassEnd = true;
        isInRenderPass = false;

        var attachments = new NativeArray<AttachmentDescriptor>(depthAttachment.HasValue ? colorAttachments.Length + 1 : colorAttachments.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        for (var i = 0; i < colorAttachments.Length; i++)
        {
            var colorAttachment = colorAttachments[i];
            if (colorAttachment.isFrameBufferOutput)
            {
                // TODO: what happens if we use don't care for frameBuffer output, does it still get stored (possibly in an optimal way?)
                attachments[i] = new(colorAttachment.frameBufferFormat)
                {
                    storeAction = RenderBufferStoreAction.Store,
                    loadStoreTarget = colorAttachment.target
                };
            }
            else
            {
                var handleData = renderGraph.RtHandleSystem.GetHandleData(colorAttachment.handle);
                var attachment = new AttachmentDescriptor(handleData.descriptor.format);

                // If the handle was created before this native render pass started, we need to load the contents. Otherwise it can be cleared or discarded
                var requiresLoad = handleData.createIndex1 < startPassIndex;
                if (requiresLoad)
                    attachment.loadAction = RenderBufferLoadAction.Load;
                else if (handleData.descriptor.clear)
                {
                    attachment.loadAction = RenderBufferLoadAction.Clear;
                    attachment.clearColor = handleData.descriptor.clearColor;
                }

                // If the handle gets freed before this native render pass ends, we can discard the contents, otherwise they must be stored as another pass is going to use it
                var requiresStore = handleData.freeIndex1 > endPassIndex;
                if (requiresStore)
                    attachment.storeAction = RenderBufferStoreAction.Store;

                // If the handle is created and freed during the renderpass, we can avoid allocating a target entirely. (TODO: The render target system may still create a texture which may be unused)
                if (requiresLoad || requiresStore)
                    attachment.loadStoreTarget = colorAttachment.target;

                attachments[i] = attachment;
            }
        }

        // Handle depth buffer if assigned
        if (depthAttachment.HasValue)
        {
            var handleData = renderGraph.RtHandleSystem.GetHandleData(depthAttachment.Value.handle);
            var attachment = new AttachmentDescriptor(handleData.descriptor.format);

            // If the handle was created before this native render pass started, we need to load the contents. Otherwise it can be cleared or discarded
            var requiresLoad = handleData.createIndex1 < startPassIndex;
            if (requiresLoad)
                attachment.loadAction = RenderBufferLoadAction.Load;
            else if (handleData.descriptor.clear)
            {
                attachment.loadAction = RenderBufferLoadAction.Clear;
                attachment.clearDepth = handleData.descriptor.clearDepth;
                attachment.clearStencil = handleData.descriptor.clearStencil;
            }

            // If the handle gets freed before this native render pass ends, we can discard the contents, otherwise they must be stored as another pass is going to use it
            var requiresStore = handleData.freeIndex1 > endPassIndex;
            if (requiresStore)
                attachment.storeAction = RenderBufferStoreAction.Store;

            // If the handle is created and freed during the renderpass, we can avoid allocating a target entirely. (TODO: The render target system may still create a texture which may be unused)
            if (requiresLoad || requiresStore)
                attachment.loadStoreTarget = depthAttachment.Value.target;

            attachments[^1] = attachment;
        }

        renderPassDescriptors.Add(new(size.x, size.y, attachments, new(subPasses.AsArray(), Allocator.Temp), size.z, 1, depthAttachment.HasValue ? colorAttachments.Length : -1, -1, passName));

        colorAttachments.Clear();
        subPasses.Clear();
        depthAttachment = null;
        passName = null;
    }

    private void EndSubPass()
    {
        var subPassInputs = new AttachmentIndexArray(inputs.Length);
        {
            for (var i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                var index = -1;
                for (var j = 0; j < colorAttachments.Length; j++)
                    if (colorAttachments[j].target == input.target)
                    {
                        index = j;
                        break;
                    }

                if (index == -1)
                {
                    index = colorAttachments.Length;
                    colorAttachments.Add(input);
                }

                subPassInputs[i] = index;
            }
        }

        var subPassOutputs = new AttachmentIndexArray(outputs.Length);
        {
            for (var i = 0; i < outputs.Length; i++)
            {
                var output = outputs[i];
                var index = -1;
                for (var j = 0; j < colorAttachments.Length; j++)
                    if (colorAttachments[j].target == output.target)
                    {
                        index = j;
                        break;
                    }

                if (index == -1)
                {
                    index = colorAttachments.Length;
                    colorAttachments.Add(output);
                }

                subPassOutputs[i] = index;
            }
        }

        subPasses.Add(new()
        {
            inputs = subPassInputs,
            colorOutputs = subPassOutputs,
            flags = flags
        });

        outputs.Clear();
        inputs.Clear();
        flags = SubPassFlags.None;
    }

    public void CreateNativeRenderPasses(List<RenderPass> renderPasses)
    {
        renderPassDescriptors.Clear();

        RenderPass previousPass = null;
        foreach (var pass in renderPasses)
        {
            var canMergePass = false;
            if (pass.IsNativeRenderPass)
            {
                Int3 passSize;
                if (pass.OutputsToCameraTarget)
                {
                    passSize = pass.FrameBufferSize;
                }
                else
                {
                    var target = renderGraph.RtHandleSystem.GetResource(pass.depthBuffer ?? pass.colorTargets[0]);
                    passSize = new(target.width, target.height, target.volumeDepth);
                }

                // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
                canMergePass = isInRenderPass && size == passSize;

                if (canMergePass)
                {
                    // We allow some subpasses to have no depth attachment, by setting the flags to readonlydepthstencil
                    if (depthAttachment.HasValue && !pass.depthBuffer.HasValue)
                    {
                        pass.flags |= SubPassFlags.ReadOnlyDepthStencil;
                    }

                    if (!depthAttachment.HasValue && pass.depthBuffer.HasValue)
                    {
                        // TODO: In this case, we should set the current as the depth attachment and set previous passes to readonly depth stencil
                        canMergePass = false;
                    }

                    if (depthAttachment.HasValue && pass.depthBuffer.HasValue && depthAttachment.Value.target != new RenderTargetIdentifier(renderGraph.RtHandleSystem.GetResource(pass.depthBuffer.Value), pass.MipLevel, pass.CubemapFace, pass.DepthSlice))
                    {
                        // If both passes have depth texutres that do not match, do not merge them
                        canMergePass = false;
                    }
                }

                if (canMergePass)
                {
                    // If flags and attachements are identical, keep using the same subpass
                    var inputCount = pass.frameBufferInputs.Count;
                    var outputCount = pass.OutputsToCameraTarget ? 1 : pass.colorTargets.Count;

                    var canPassMergeWithSubPass = pass.flags == flags && inputCount == inputs.Length && outputCount == outputs.Length;
                    if (canPassMergeWithSubPass)
                    {
                        // Check if all the inputs and outputs are equal (And in identical order) since this must be true for the pass to merge
                        for (var i = 0; i < inputCount; i++)
                        {
                            var loadStoreTarget = renderGraph.RtHandleSystem.GetResource(pass.frameBufferInputs[i]);
                            if (loadStoreTarget == inputs[i].target)
                                continue;

                            canPassMergeWithSubPass = false;
                            break;
                        }

                        if (canPassMergeWithSubPass)
                        {
                            if (pass.OutputsToCameraTarget)
                            {
                                if (pass.FrameBufferTarget != outputs[0].target)
                                    canPassMergeWithSubPass = false;
                            }
                            else
                            {
                                for (var i = 0; i < pass.colorTargets.Count; i++)
                                {
                                    var target = renderGraph.RtHandleSystem.GetResource(pass.colorTargets[i]);
                                    var targetIdentifier = new RenderTargetIdentifier(target, pass.MipLevel, pass.CubemapFace, pass.DepthSlice);
                                    if (targetIdentifier == outputs[i].target)
                                        continue;

                                    canPassMergeWithSubPass = false;
                                    break;
                                }
                            }
                        }
                    }

                    if (!canPassMergeWithSubPass)
                    {
                        if (pass.AllowNewSubPass)
                        {
                            EndSubPass();
                            pass.IsNextSubPass = true;
                            BeginSubpass(pass);
                        }
                        else
                        {
                            canMergePass = false;
                        }
                    }
                }
            }

            if (!canMergePass)
            {
                if (isInRenderPass)
                    EndRenderPass(previousPass);

                if (pass.IsNativeRenderPass)
                {
                    BeginRenderPass(pass);
                    BeginSubpass(pass);
                }
            }

            previousPass = pass;
        }

        // The frame may end on a final renderpass, in which case we need to end it
        if (isInRenderPass)
            EndRenderPass(previousPass);
    }
}
