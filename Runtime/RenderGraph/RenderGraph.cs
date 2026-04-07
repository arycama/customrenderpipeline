using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RenderGraph : IDisposable
{
    private readonly Dictionary<Type, Stack<RenderPass>> renderPassPool = new();

    private bool disposedValue;
    private readonly List<RenderPass> renderPasses = new();
    private readonly List<int> renderPassIndices = new();
    private readonly List<int> subPassIndices = new();

    private readonly GraphicsBuffer emptyBuffer;
    private readonly RenderTexture emptyTexture, emptyUavTexture, emptyTextureArray, empty3DTexture, emptyCubemap, emptyCubemapArray;
    private readonly Dictionary<Type, RTHandleData> rtHandles = new();

    private Int2 size;
    private int viewCount;
    private int mipLevel;
    private AttachmentData? subPassDepth;
    private bool isInRenderPass;
    private string passName;
    private int depthIndex = -1;
    private int subPassIndex = -1;

    private NativeList<AttachmentData> inputs = new(8, Allocator.Persistent), outputs = new(8, Allocator.Persistent);
    private SubPassFlags flags;

    private int startPassIndex, endPassIndex;

    private readonly NativeList<AttachmentData> attachments = new(8, Allocator.Persistent);
    private readonly NativeList<SubPassDescriptor> subPasses = new(Allocator.Persistent);
    private readonly List<RenderPassDescriptor> renderPassDescriptors = new();

    private RenderPass previousPass, currentPass;

    public RTHandleSystem RtHandleSystem { get; }
    public BufferHandleSystem BufferHandleSystem { get; }
    public RenderResourceMap ResourceMap { get; } = new();
    public CustomRenderPipelineBase RenderPipeline { get; }

    public ResourceHandle<GraphicsBuffer> EmptyBuffer { get; }
    public ResourceHandle<RenderTexture> EmptyTexture { get; }
    public ResourceHandle<RenderTexture> EmptyUavTexture { get; }
    public ResourceHandle<RenderTexture> EmptyTextureArray { get; }
    public ResourceHandle<RenderTexture> Empty3DTexture { get; }
    public ResourceHandle<RenderTexture> EmptyCubemap { get; }
    public ResourceHandle<RenderTexture> EmptyCubemapArray { get; }

    public int FrameIndex { get; private set; }
    public bool IsExecuting { get; private set; }
    public bool CullRtHandles { get; set; }

    public RenderGraph(CustomRenderPipelineBase renderPipeline)
    {
        RtHandleSystem = new();
        BufferHandleSystem = new();

        emptyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(int)) { name = "Empty Structured Buffer" };
        emptyTexture = new RenderTexture(1, 1, 0) { hideFlags = HideFlags.HideAndDontSave, };
        emptyUavTexture = new RenderTexture(1, 1, 0) { hideFlags = HideFlags.HideAndDontSave, enableRandomWrite = true };
        emptyTextureArray = new RenderTexture(1, 1, 0) { dimension = TextureDimension.Tex2DArray, volumeDepth = 1, hideFlags = HideFlags.HideAndDontSave };
        empty3DTexture = new RenderTexture(1, 1, 0) { dimension = TextureDimension.Tex3D, volumeDepth = 1, hideFlags = HideFlags.HideAndDontSave };
        emptyCubemap = new RenderTexture(1, 1, 0) { dimension = TextureDimension.Cube, hideFlags = HideFlags.HideAndDontSave };
        emptyCubemapArray = new RenderTexture(1, 1, 0) { dimension = TextureDimension.CubeArray, volumeDepth = 6, hideFlags = HideFlags.HideAndDontSave };

        EmptyBuffer = BufferHandleSystem.ImportResource(emptyBuffer);
        EmptyTexture = RtHandleSystem.ImportResource(emptyTexture);
        EmptyUavTexture = RtHandleSystem.ImportResource(emptyUavTexture);
        EmptyTextureArray = RtHandleSystem.ImportResource(emptyTextureArray);
        Empty3DTexture = RtHandleSystem.ImportResource(empty3DTexture);
        EmptyCubemap = RtHandleSystem.ImportResource(emptyCubemap);
        EmptyCubemapArray = RtHandleSystem.ImportResource(emptyCubemapArray);

        RenderPipeline = renderPipeline;
    }

    ~RenderGraph()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue)
            return;

        if (!disposing)
            Debug.LogError("Render Graph not disposed correctly");

        emptyBuffer.Dispose();
        Object.DestroyImmediate(emptyTexture);
        Object.DestroyImmediate(emptyUavTexture);
        Object.DestroyImmediate(emptyTextureArray);
        Object.DestroyImmediate(empty3DTexture);
        Object.DestroyImmediate(emptyCubemap);
        Object.DestroyImmediate(emptyCubemapArray);

        ResourceMap.Dispose();
        RtHandleSystem.Dispose();
        BufferHandleSystem.Dispose();

        inputs.Dispose();
        outputs.Dispose();

        disposedValue = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public T AddRenderPass<T>(string name) where T : RenderPass, new()
    {
        if (!renderPassPool.TryGetValue(typeof(T), out var pool))
        {
            pool = new();
            renderPassPool.Add(typeof(T), pool);
        }

        if (!pool.TryPop(out var result))
            result = new T();

        result.Reset();
        result.RenderGraph = this;
        result.Name = name;
        result.Index = renderPasses.Count;

        renderPasses.Add(result);

        if (currentPass != null)
        {
            var canMergePass = false;
            if (currentPass.IsNativeRenderPass)
            {
                // Passes can merge if they have the same size and depth attachment. (But may require seperate subpasses if color attachments or flags differ)
                canMergePass = isInRenderPass && size == currentPass.Size && viewCount == currentPass.ViewCount && mipLevel == currentPass.MipLevel;

                if (canMergePass)
                {
                    // We allow some subpasses to have no depth attachment, by setting the flags to readonlydepthstencil
                    if (!currentPass.depthBuffer.HasValue)
                    {
                        currentPass.flags = SubPassFlags.ReadOnlyDepthStencil;
                    }

                    if (depthIndex != -1 && currentPass.depthBuffer.HasValue)
                    {
                        var depthAttachment = attachments[depthIndex];
                        if (depthAttachment.handle != currentPass.depthBuffer.Value || depthAttachment.mipLevel != currentPass.MipLevel || depthAttachment.cubemapFace != currentPass.CubemapFace || depthAttachment.depthSlice != currentPass.DepthSlice)
                        {
                            // If both passes have depth texutres that do not match, do not merge them
                            canMergePass = false;
                        }
                    }
                }

                if (canMergePass)
                {
                    // If flags and attachements are identical, keep using the same subpass
                    var inputCount = currentPass.frameBufferInputs.Count;
                    var outputCount = currentPass.OutputsToCameraTarget ? 1 : currentPass.colorTargets.Count;

                    var canMergeSubPass = subPasses.Length < 8 && currentPass.flags == flags && inputCount == inputs.Length && outputCount == outputs.Length;
                    if (canMergeSubPass)
                    {
                        // Check if all the inputs and outputs are equal (And in identical order) since this must be true for the pass to merge
                        for (var i = 0; i < inputCount; i++)
                        {
                            var input = inputs[i];
                            if (currentPass.frameBufferInputs[i] == input.handle && currentPass.MipLevel == input.mipLevel && currentPass.CubemapFace == input.cubemapFace && currentPass.DepthSlice == input.depthSlice)
                                continue;

                            canMergeSubPass = false;
                            break;
                        }

                        if (canMergeSubPass)
                        {
                            if (currentPass.OutputsToCameraTarget)
                            {
                                if (currentPass.FrameBufferTarget != outputs[0].frameBufferTarget)
                                    canMergeSubPass = false;
                            }
                            else
                            {
                                for (var i = 0; i < currentPass.colorTargets.Count; i++)
                                {
                                    var output = outputs[i];
                                    if (currentPass.colorTargets[i] == output.handle && currentPass.MipLevel == output.mipLevel && currentPass.CubemapFace == output.cubemapFace && currentPass.DepthSlice == output.depthSlice)
                                        continue;

                                    canMergeSubPass = false;
                                    break;
                                }
                            }
                        }
                    }

                    if (!canMergeSubPass)
                    {
                        if (currentPass.PreventNewSubPass)
                        {
                            canMergePass = false;
                        }
                        else
                        {
                            EndSubPass();
                            BeginSubpass(currentPass);
                        }
                    }
                }
            }

            if (!canMergePass)
            {
                if (isInRenderPass)
                    EndRenderPass(previousPass);

                if (currentPass.IsNativeRenderPass)
                {
                    // Begin render pass
                    BeginRenderPass(currentPass);
                    BeginSubpass(currentPass);
                }
            }

            previousPass = currentPass;

            // If this is the first pass, either add 0 (Eg current count) or -1 if not a native render pass
            if (previousPass.IsNativeRenderPass)
                renderPassIndices.Add(renderPassDescriptors.Count);
            else
                renderPassIndices.Add(-1);

            subPassIndices.Add(subPassIndex);
        }

        currentPass = result;

        return (T)result;
    }

    public T AddRenderPass<T, K>(string name, K data) where T : RenderPass<K>, new()
    {
        var result = AddRenderPass<T>(name);
        result.renderData = data;
        return result;
    }

    public void SetRTHandle<T>(ResourceHandle<RenderTexture> handle, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default) where T : struct, IRtHandleId
    {
        var data = new T();
        rtHandles[typeof(T)] = new RTHandleData(handle, data.PropertyId, data.ScaleLimitPropertyId, mip, subElement);
    }

    public RTHandleData GetRTHandle(Type type)
    {
        return rtHandles[type];
    }

    public bool TryGetRTHandle<T>(out ResourceHandle<RenderTexture> handle) where T : IRtHandleId
    {
        var result = rtHandles.TryGetValue(typeof(T), out var data);
        handle = data.handle;
        return result;
    }

    private void BeginRenderPass(RenderPass pass)
    {
        isInRenderPass = true;
        startPassIndex = pass.Index;
        passName = pass.Name;
        size = pass.Size;
        viewCount = pass.ViewCount;
        mipLevel = pass.MipLevel;
    }

    private void BeginSubpass(RenderPass pass)
    {
        flags = pass.flags;
        subPassIndex++;

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
        isInRenderPass = false;
        renderPassDescriptors.Add(new(size, new(attachments.AsArray(), Allocator.Temp), new(subPasses.AsArray(), Allocator.Temp), startPassIndex, endPassIndex, pass.ViewCount, 1, depthIndex, -1, passName));
        attachments.Clear();
        subPasses.Clear();
        passName = null;
        depthIndex = -1;
        subPassIndex = -1;
    }

    public void Execute(CommandBuffer command, ScriptableRenderContext context)
    {
        subPassIndices.Add(subPassIndex);

        if (previousPass.IsNativeRenderPass)
            renderPassIndices.Add(renderPassDescriptors.Count);
        else
            renderPassIndices.Add(-1);

        currentPass = null;

        // The frame may end on a final renderpass, in which case we need to end it
        if (isInRenderPass)
            EndRenderPass(previousPass);

        previousPass = null;

        // Check which resources need to be allocated
        if (CullRtHandles)
        {
            foreach (var descriptor in renderPassDescriptors)
            {
                for (var i = 0; i < descriptor.attachments.Length; i++)
                {
                    var colorAttachment = descriptor.attachments[i];
                    if (colorAttachment.isFrameBufferOutput)
                        continue;

                    var handleData = RtHandleSystem.GetHandleData(colorAttachment.handle);
                    if (handleData.freeIndex > descriptor.endPassIndex || handleData.freeIndex == -1)
                        continue;

                    handleData.isUsed = false;
                    handleData.createIndex1 = handleData.createIndex;
                    handleData.freeIndex1 = handleData.freeIndex;
                    RtHandleSystem.SetHandleData(colorAttachment.handle, handleData);
                }
            }
        }

        BufferHandleSystem.AllocateFrameResources(renderPasses.Count, FrameIndex);
        RtHandleSystem.AllocateFrameResources(renderPasses.Count, FrameIndex);

        IsExecuting = true;

        var previousNativePassIndex = -1;
        var previousSubPassIndex = -1;
        for (var i = 0; i < renderPasses.Count; i++)
        {
            var renderPass = renderPasses[i];
            var currentNativePassIndex = renderPassIndices[i];
            var currentSubPassIndex = subPassIndices[i];

            // Begin a new pass if the index has increased
            if (currentNativePassIndex != -1)
            {
                if (currentNativePassIndex != previousNativePassIndex)
                {
                    renderPassDescriptors[currentNativePassIndex].BeginRenderPass(command, this);
                    previousSubPassIndex = 0;
                }

                // Incremnt subpass if needed
                if (currentSubPassIndex > previousSubPassIndex)
                    command.NextSubPass();
            }

            renderPass.Run(command, context);

            // End pass if needed
            if (i < renderPasses.Count - 1)
            {
                var nextNativePassIndex = renderPassIndices[i + 1];
                if (currentNativePassIndex != -1 && nextNativePassIndex != currentNativePassIndex)
                    command.EndRenderPass();
            }
            else if (currentNativePassIndex != -1)
            {
                command.EndRenderPass();
            }

            // TODO: Handle differently?
            renderPass.PostExecute();

            previousNativePassIndex = currentNativePassIndex;
            previousSubPassIndex = currentSubPassIndex;
        }

        renderPassIndices.Clear();
        subPassIndices.Clear();
        renderPassDescriptors.Clear();

        foreach (var renderPass in renderPasses)
            renderPass.Reset();

        // Re-add the passes to the pool
        foreach (var renderPass in renderPasses)
        {

            if (!renderPassPool.TryGetValue(renderPass.GetType(), out var pool))
            {
                pool = new();
                renderPassPool.Add(renderPass.GetType(), pool);
            }

            pool.Push(renderPass);
        }

        IsExecuting = false;
    }

    public void CleanupCurrentFrame()
    {
        renderPasses.Clear();
        BufferHandleSystem.CleanupCurrentFrame(FrameIndex);
        RtHandleSystem.CleanupCurrentFrame(FrameIndex);

        if (!FrameDebugger.enabled)
            FrameIndex++;
    }
}
