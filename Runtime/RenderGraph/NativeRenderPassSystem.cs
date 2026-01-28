using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Rendering;

public class NativeRenderPassSystem : IDisposable
{
    private Int3 size;
    private AttachmentDescriptor? depthAttachment;
    private bool isInRenderPass;
    private string passName;

    private NativeList<AttachmentDescriptor> inputs = new(8, Allocator.Persistent), outputs = new(8, Allocator.Persistent);
    private SubPassFlags flags;

    private readonly NativeList<AttachmentDescriptor> colorAttachments = new(8, Allocator.Persistent);
    private readonly NativeList<SubPassDescriptor> subPasses = new(Allocator.Persistent);
    private readonly List<RenderPassDescriptor> renderPassDescriptors = new();

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
        size = pass.size;
        depthAttachment = pass.depthAttachment;
        flags = pass.flags;
    }

    private void BeginSubpass(RenderPass pass)
    {
        inputs.CopyFrom(pass.inputs.AsArray());
        outputs.CopyFrom(pass.outputs.AsArray());
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
        foreach (var renderPass in renderPasses)
            renderPass.SetupRenderPassData();

        renderPassDescriptors.Clear();

        RenderPass previousPass = null;
        foreach (var pass in renderPasses)
        {
            var canMergePass = false;
            if (pass.IsNativeRenderPass)
            {
                // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
                canMergePass = isInRenderPass && size == pass.size;

                if (canMergePass)
                {
                    // We allow some subpasses to have no depth attachment, by setting the flags to readonlydepthstencil
                    if (depthAttachment.HasValue && !pass.depthAttachment.HasValue)
                    {
                        pass.flags |= SubPassFlags.ReadOnlyDepthStencil;
                    }

                    if (!depthAttachment.HasValue && pass.depthAttachment.HasValue)
                    {
                        // TODO: In this case, we should set the current as the depth attachment and set previous passes to readonly depth stencil
                        canMergePass = false;
                    }

                    if (depthAttachment.HasValue && pass.depthAttachment.HasValue && depthAttachment.Value.loadStoreTarget != pass.depthAttachment.Value.loadStoreTarget)
                    {
                        // If both passes have depth texutres that do not match, do not merge them
                        canMergePass = false;
                    }
                }

                if (canMergePass)
                {
                    // If flags and attachements are identical, keep using the same subpass
                    var canPassMergeWithSubPass = pass.flags == flags && pass.inputs.Length == inputs.Length && pass.outputs.Length == outputs.Length;
                    if (canPassMergeWithSubPass)
                    {
                        // Check if all the inputs and outputs are equal (And in identical order) since this must be true for the pass to merge
                        for (var i = 0; i < pass.inputs.Length; i++)
                        {
                            if (pass.inputs[i].loadStoreTarget == inputs[i].loadStoreTarget)
                                continue;

                            canPassMergeWithSubPass = false;
                            break;
                        }

                        if (canPassMergeWithSubPass)
                        {
                            for (var i = 0; i < pass.outputs.Length; i++)
                            {
                                if (pass.outputs[i].loadStoreTarget == outputs[i].loadStoreTarget)
                                    continue;

                                canPassMergeWithSubPass = false;
                                break;
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