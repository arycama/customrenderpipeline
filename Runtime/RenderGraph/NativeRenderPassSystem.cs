using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class NativeRenderPassSystem : IDisposable
{
    private readonly RenderGraph renderGraph;

    private Int3 size;
    private AttachmentDescriptor? depthAttachment;
    private bool isInRenderPass;
    private string passName;

    private NativeList<AttachmentDescriptor> inputs = new(8, Allocator.Persistent), outputs = new(8, Allocator.Persistent);
    private SubPassFlags flags;

    private readonly NativeList<AttachmentDescriptor> colorAttachments = new(8, Allocator.Persistent);
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

        pass.IsRenderPassStart = true;
        pass.RenderPassIndex = renderPassDescriptors.Count;

        passName = pass.Name;
        flags = pass.flags;

        if(pass.OutputsToCameraTarget)
        {
            size = pass.FrameBufferSize;
        }
        else if (pass.depthBuffer.HasValue)
        {
            var handleData = renderGraph.RtHandleSystem.GetHandleData(pass.depthBuffer.Value);
            var target = renderGraph.RtHandleSystem.GetResource(pass.depthBuffer.Value);

            depthAttachment = new AttachmentDescriptor(handleData.descriptor.format)
            {
                loadAction = handleData.createIndex1 == pass.Index ? handleData.descriptor.clear ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load,
                storeAction = handleData.freeIndex1 == pass.Index ? RenderBufferStoreAction.DontCare : RenderBufferStoreAction.Store,
                loadStoreTarget = new RenderTargetIdentifier(target, pass.MipLevel, pass.CubemapFace, pass.DepthSlice),
                clearColor = handleData.descriptor.clearColor
            };

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
            var handleData = renderGraph.RtHandleSystem.GetHandleData(input);

            var target = renderGraph.RtHandleSystem.GetResource(input);
            var descriptor = new AttachmentDescriptor(handleData.descriptor.format)
            {
                loadAction = RenderBufferLoadAction.Load,
                storeAction = handleData.freeIndex1 == pass.Index ? RenderBufferStoreAction.DontCare : RenderBufferStoreAction.Store,
                loadStoreTarget = new RenderTargetIdentifier(target, 0, CubemapFace.Unknown, -1),
            };

            inputs.Add(descriptor);
        }

        if (pass.OutputsToCameraTarget)
        {
            var descriptor = new AttachmentDescriptor(pass.FrameBufferFormat) { loadStoreTarget = pass.FrameBufferTarget, storeAction = RenderBufferStoreAction.Store };
            outputs.Add(descriptor);
        }
        else
        {
            if (pass.depthBuffer.HasValue)
            {
                var handleData = renderGraph.RtHandleSystem.GetHandleData(pass.depthBuffer.Value);
                var target = renderGraph.RtHandleSystem.GetResource(pass.depthBuffer.Value);
                var descriptor = new AttachmentDescriptor(handleData.descriptor.format)
                {
                    loadAction = handleData.createIndex1 == pass.Index ? handleData.descriptor.clear ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load,
                    storeAction = handleData.freeIndex1 == pass.Index ? RenderBufferStoreAction.DontCare : RenderBufferStoreAction.Store,
                    loadStoreTarget = new RenderTargetIdentifier(target, pass.MipLevel, pass.CubemapFace, pass.DepthSlice),
                    clearColor = handleData.descriptor.clearColor
                };

                depthAttachment = descriptor;
            }

            foreach (var colorTarget in pass.colorTargets)
            {
                var handleData = renderGraph.RtHandleSystem.GetHandleData(colorTarget);

                var target = renderGraph.RtHandleSystem.GetResource(colorTarget);
                var descriptor = new AttachmentDescriptor(handleData.descriptor.format)
                {
                    loadAction = handleData.createIndex1 == pass.Index ? handleData.descriptor.clear ? RenderBufferLoadAction.Clear : RenderBufferLoadAction.DontCare : RenderBufferLoadAction.Load,
                    storeAction = handleData.freeIndex1 == pass.Index ? RenderBufferStoreAction.DontCare : RenderBufferStoreAction.Store,
                    loadStoreTarget = new RenderTargetIdentifier(target, pass.MipLevel, pass.CubemapFace, pass.DepthSlice),
                    clearColor = handleData.descriptor.clearColor
                };

                outputs.Add(descriptor);
            }
        }
    }

    private void EndRenderPass(RenderPass pass)
    {
        EndSubPass();

        pass.IsRenderPassEnd = true;
        isInRenderPass = false;

        var depthIndex = -1;
        if (depthAttachment.HasValue)
        {
            depthIndex = colorAttachments.Length;
            colorAttachments.Add(depthAttachment.Value);
        }

        renderPassDescriptors.Add(new(size.x, size.y, new(colorAttachments.AsArray(), Allocator.Temp), new(subPasses.AsArray(), Allocator.Temp), size.z, 1, depthIndex, -1, passName));

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
                    if (colorAttachments[j].loadStoreTarget == input.loadStoreTarget)
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
                    if (colorAttachments[j].loadStoreTarget == output.loadStoreTarget)
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
                if(pass.OutputsToCameraTarget)
                {
                    passSize = pass.FrameBufferSize;
                }
                else
                {
                    var target = renderGraph.RtHandleSystem.GetResource(pass.depthBuffer.HasValue ? pass.depthBuffer.Value : pass.colorTargets[0]);
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

                    if (depthAttachment.HasValue && pass.depthBuffer.HasValue && depthAttachment.Value.loadStoreTarget != new RenderTargetIdentifier(renderGraph.RtHandleSystem.GetResource(pass.depthBuffer.Value), pass.MipLevel, pass.CubemapFace, pass.DepthSlice))
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
                            if (loadStoreTarget == inputs[i].loadStoreTarget)
                                continue;

                            canPassMergeWithSubPass = false;
                            break;
                        }

                        if (canPassMergeWithSubPass)
                        {
                            if (pass.OutputsToCameraTarget)
                            {
                                if (pass.FrameBufferTarget != outputs[0].loadStoreTarget)
                                    canPassMergeWithSubPass = false;
                            }
                            else
                            {
                                for (var i = 0; i < pass.colorTargets.Count; i++)
                                {
                                    var target = renderGraph.RtHandleSystem.GetResource(pass.colorTargets[i]);
                                    var targetIdentifier = new RenderTargetIdentifier(target, pass.MipLevel, pass.CubemapFace, pass.DepthSlice);
                                    if (targetIdentifier == outputs[i].loadStoreTarget)
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