using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class NativeRenderPassSystem : IDisposable
{
    private readonly RenderGraph renderGraph;

    private Int2 size;
    private int viewCount;
    private AttachmentData? subPassDepth;
    private bool isInRenderPass;
    private string passName;
    private int depthIndex = -1;

    private NativeList<AttachmentData> inputs = new(8, Allocator.Persistent), outputs = new(8, Allocator.Persistent);
    private SubPassFlags flags;

    private int startPassIndex, endPassIndex;

    private readonly NativeList<AttachmentData> attachments = new(8, Allocator.Persistent);
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
        size = pass.Size;
        viewCount = pass.ViewCount;
    }

    private void BeginSubpass(RenderPass pass)
    {
        flags = pass.flags;

        foreach (var input in pass.frameBufferInputs)
        {
            // TODO: Why are these just using default values?
            inputs.Add(new(input, default, default, false, 0, CubemapFace.Unknown, -1));
        }

        if (pass.OutputsToCameraTarget)
        {
            outputs.Add(new(default, pass.FrameBufferTarget, pass.FrameBufferFormat, true, 0, CubemapFace.Unknown, -1));
        }
        else
        {
            if (pass.depthBuffer.HasValue)
            {
                // If depth is not assigned, assign it, otherwise ensure it matches
                if (depthIndex == -1)
                {
                    subPassDepth = new(pass.depthBuffer.Value, default, default, false, pass.MipLevel, pass.CubemapFace, pass.DepthSlice);
                }
                else
                {
                    Assert.IsTrue(attachments[depthIndex].handle == pass.depthBuffer.Value);
                }
            }

            foreach (var colorTarget in pass.colorTargets)
            {
                outputs.Add(new(colorTarget, default, default, false, pass.MipLevel, pass.CubemapFace, pass.DepthSlice));
            }
        }
    }

    private void EndSubPass()
    {
        // Add depth first if needed
        if (subPassDepth.HasValue)
        {
            var input = subPassDepth.Value;
            var index = -1;
            for (var j = 0; j < attachments.Length; j++)
                if (attachments[j].handle == input.handle)
                {
                    index = j;
                    break;
                }

            if (index == -1)
            {
                index = attachments.Length;
                attachments.Add(input);
            }

            depthIndex = index;
        }

        var subPassInputs = new AttachmentIndexArray(inputs.Length);
        {
            for (var i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                var index = -1;
                for (var j = 0; j < attachments.Length; j++)
                    if (attachments[j].handle == input.handle)
                    {
                        index = j;
                        break;
                    }

                if (index == -1)
                {
                    index = attachments.Length;
                    attachments.Add(input);
                }

                subPassInputs[i] = index;
            }
        }

        var subPassOutputs = new AttachmentIndexArray(outputs.Length);
        {
            // Find the index of each output in the existing attachments array if it exists
            for (var i = 0; i < outputs.Length; i++)
            {
                var output = outputs[i];
                var index = -1;
                for (var j = 0; j < attachments.Length; j++)
                {
                    var attachment = attachments[j];

                    if (output.isFrameBufferOutput)
                    {
                        // If this is a framebuffer output, the existing attachment must also be a framebuffer attachment pointing to the same target
                        if (!attachment.isFrameBufferOutput)
                            continue;

                        if (attachment.frameBufferTarget != output.frameBufferTarget)
                            continue;
                    }
                    else
                    {
                        // Otherwise check if the handles match
                        if (output.handle != attachment.handle)
                            continue;
                    }

                    index = j;
                    break;
                }

                if (index == -1)
                {
                    index = attachments.Length;
                    attachments.Add(output);
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
        subPassDepth = null;
    }

    private void EndRenderPass(RenderPass pass)
    {
        EndSubPass();

        endPassIndex = pass.Index;

        pass.IsRenderPassEnd = true;
        isInRenderPass = false;

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

        renderPassDescriptors.Add(new(size, attachments, new(subPasses.AsArray(), Allocator.Temp), pass.ViewCount, 1, depthIndex, -1, passName));

        this.attachments.Clear();
        subPasses.Clear();
        passName = null;
        depthIndex = -1;
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
                // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
                canMergePass = isInRenderPass && size == pass.Size && viewCount == pass.ViewCount;

                if (canMergePass)
                {
                    // We allow some subpasses to have no depth attachment, by setting the flags to readonlydepthstencil
                    if (!pass.depthBuffer.HasValue)
                    {
                        pass.flags = SubPassFlags.ReadOnlyDepthStencil;
                    }

                    if (depthIndex != -1 && pass.depthBuffer.HasValue)
                    {
                        var depthAttachment = attachments[depthIndex];
                        if (depthAttachment.handle != pass.depthBuffer.Value || depthAttachment.mipLevel != pass.MipLevel || depthAttachment.cubemapFace != pass.CubemapFace || depthAttachment.depthSlice != pass.DepthSlice)
                        {
                            // If both passes have depth texutres that do not match, do not merge them
                            canMergePass = false;
                        }
                    }
                }

                if (canMergePass)
                {
                    // If flags and attachements are identical, keep using the same subpass
                    var inputCount = pass.frameBufferInputs.Count;
                    var outputCount = pass.OutputsToCameraTarget ? 1 : pass.colorTargets.Count;

                    var canPassMergeWithSubPass = subPasses.Length < 8 && pass.flags == flags && inputCount == inputs.Length && outputCount == outputs.Length;
                    if (canPassMergeWithSubPass)
                    {
                        // Check if all the inputs and outputs are equal (And in identical order) since this must be true for the pass to merge
                        for (var i = 0; i < inputCount; i++)
                        {
                            var input = inputs[i];
                            if (pass.frameBufferInputs[i] == input.handle && pass.MipLevel == input.mipLevel && pass.CubemapFace == input.cubemapFace && pass.DepthSlice == input.depthSlice)
                                continue;

                            canPassMergeWithSubPass = false;
                            break;
                        }

                        if (canPassMergeWithSubPass)
                        {
                            if (pass.OutputsToCameraTarget)
                            {
                                if (pass.FrameBufferTarget != outputs[0].frameBufferTarget)
                                    canPassMergeWithSubPass = false;
                            }
                            else
                            {
                                for (var i = 0; i < pass.colorTargets.Count; i++)
                                {
                                    var output = outputs[i];
                                    if (pass.colorTargets[i] == output.handle && pass.MipLevel == output.mipLevel && pass.CubemapFace == output.cubemapFace && pass.DepthSlice == output.depthSlice)
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
