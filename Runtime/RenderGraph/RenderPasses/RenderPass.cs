using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public abstract class RenderPass<T> : RenderPass
{
    private Action<CommandBuffer, RenderPass> defaultRenderFunction;
    private Action<CommandBuffer, RenderPass, T> renderFunction;
    public T renderData;

    public override void Reset()
    {
        base.Reset();
        defaultRenderFunction = null;
        renderFunction = null;
        renderData = default;
    }

    protected override void ExecuteRenderPassBuilder()
    {
        if (defaultRenderFunction != null)
            defaultRenderFunction(Command, this);
        else
            renderFunction?.Invoke(Command, this, renderData);
    }

    public void SetRenderFunction(Action<CommandBuffer, RenderPass> renderFunction)
    {
        defaultRenderFunction = renderFunction;
    }

    public void SetRenderFunction(Action<CommandBuffer, RenderPass, T> renderFunction)
    {
        this.renderFunction = renderFunction;
    }
}

public abstract class RenderPass : IDisposable
{
    // TODO: Convert to handles and remove
    protected readonly List<(int, ResourceHandle<RenderTexture>, int, RenderTextureSubElement)> readTextures = new();
    protected readonly List<(string, ResourceHandle<GraphicsBuffer>, int size, int offset)> readBuffers = new();
    protected readonly List<(string, ResourceHandle<GraphicsBuffer>)> writeBuffers = new();

    private readonly List<(RenderPassDataHandle, bool)> RenderPassDataHandles = new();
    protected readonly List<string> keywords = new();

    public SubPassFlags flags;

    public readonly List<ResourceHandle<RenderTexture>> frameBufferInputs = new();
    public readonly List<ResourceHandle<RenderTexture>> colorTargets = new();
    public ResourceHandle<RenderTexture>? depthBuffer;

    public int DepthSlice { get; set; } = -1;
    public int MipLevel { get; set; }
    public CubemapFace CubemapFace { get; set; } = CubemapFace.Unknown;

    public RenderPass()
    {
        PropertyBlock = new();
    }

    public MaterialPropertyBlock PropertyBlock { get; private set; }
    public CommandBuffer Command { get; private set; }
    public RenderGraph RenderGraph { get; set; }
    internal string Name { get; set; }
    public int Index { get; set; }
    public bool UseProfiler { get; set; } = true;
    public virtual bool IsNativeRenderPass => false;
    public virtual bool OutputsToCameraTarget => false;
    public bool PreventNewSubPass { get; set; } = false;
    public RenderTargetIdentifier FrameBufferTarget { get; protected set; }
    public GraphicsFormat FrameBufferFormat { get; protected set; }
    public Int2 Size { get; protected set; }
    public int ViewCount { get; protected set; }
    public int AntiAliasing { get; protected set; } = 1;
    public bool IsScreenPass { get; protected set; }

    public abstract void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default);
    public abstract void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer);
    public abstract void SetVector(int propertyId, Float4 value);
    public abstract void SetVectorArray(string propertyName, Vector4[] value);
    public abstract void SetFloat(string propertyName, float value);
    public abstract void SetFloatArray(string propertyName, float[] value);
    public abstract void SetInt(string propertyName, int value);
    public abstract void SetMatrix(string propertyName, Matrix4x4 value);
    public abstract void SetMatrixArray(string propertyName, Matrix4x4[] value);
    public abstract void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value, int size, int offset);

    public void SetVector(string propertyName, Float4 value) => SetVector(Shader.PropertyToID(propertyName), value);

    protected abstract void Execute();

    protected abstract void ExecuteRenderPassBuilder();

    public virtual void Reset()
    {
        readTextures.Clear();
        readBuffers.Clear();
        writeBuffers.Clear();
        RenderPassDataHandles.Clear();
        Command = null;
        Name = null;
        Index = -1;
        UseProfiler = true;
        keywords.Clear();
        PropertyBlock.Clear();
        frameBufferInputs.Clear();
        colorTargets.Clear();
        depthBuffer = default;
        PreventNewSubPass = false;
    }

    void IDisposable.Dispose() { }

    public GraphicsBuffer GetBuffer(ResourceHandle<GraphicsBuffer> handle)
    {
        Assert.IsTrue(RenderGraph.IsExecuting);
        var result = RenderGraph.BufferHandleSystem.GetResource(handle);
        return result ?? throw new InvalidOperationException($"{Name} is trying to retrieve a GraphicsBuffer that does not exist. Handle: {handle.index} ({RenderGraph.BufferHandleSystem.GetDescriptor(handle)}");
    }

    public RenderTexture GetRenderTexture(ResourceHandle<RenderTexture> handle)
    {
        Assert.IsTrue(RenderGraph.IsExecuting);
        var result = RenderGraph.RtHandleSystem.GetResource(handle);
        return result ?? throw new InvalidOperationException($"{Name} is trying to retrieve a RenderTexture that does not exist. Handle: {handle.index} ({RenderGraph.RtHandleSystem.GetDescriptor(handle)}");
    }

    public void AddKeyword(string keyword)
    {
        keywords.Add(keyword);
    }

    public void ReadRtHandle<T>() where T : IRtHandleId
    {
        var (handle, mip, subElement) = RenderGraph.GetRtHandleData<T>();
        ReadTexture(RTHandleHolder<T>.propertyNameId, handle, mip, subElement);
    }

    public void ReadFrameBuffer<T>() where T : IRtHandleId
    {
        var index = RTHandleHolder<T>.index;
        var handleData = RTHandleHolder.GetHandleData(index);
        frameBufferInputs.Add(handleData.handle);
        RenderGraph.RtHandleSystem.ReadResource(handleData.handle, Index);
    }

    public void ReadFrameDepth<T>() where T : IRtHandleId
    {
        var index = RTHandleHolder<T>.index;
        var handleData = RTHandleHolder.GetHandleData(index);
        var handle = handleData.handle;

        if (depthBuffer == null)
        {
            depthBuffer = handle;
            flags |= SubPassFlags.ReadOnlyDepth;
        }
        else if (depthBuffer.HasValue)
        {
            if (depthBuffer.Value == handle)
            {
                flags |= SubPassFlags.ReadOnlyDepth;
            }
            else
            {
                Debug.LogError("Trying to read frame depth with a mismatched depth buffer");
            }
        }

        frameBufferInputs.Add(handle);
        RenderGraph.RtHandleSystem.ReadResource(handle, Index);
    }

    public void Run(CommandBuffer command, ScriptableRenderContext context)
    {
        Command = command;

        if (UseProfiler)
            Command.BeginSample(Name);

        // Move into some OnPreRender thing in buffer/RTHandles? 
        foreach (var texture in readTextures)
        {
            var handle = texture.Item2;
            SetTexture(texture.Item1, GetRenderTexture(handle), texture.Item3, texture.Item4);
        }

        foreach (var buffer in readBuffers)
        {
            var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(buffer.Item2);
            if (descriptor.Target.HasFlag(GraphicsBuffer.Target.Constant))
                SetConstantBuffer(buffer.Item1, buffer.Item2, buffer.size, buffer.offset);
            else
                SetBuffer(buffer.Item1, buffer.Item2);
        }

        foreach (var buffer in writeBuffers)
            SetBuffer(buffer.Item1, buffer.Item2);

        // Set any data from each pass
        foreach (var renderPassDataHandle in RenderPassDataHandles)
        {
            var hasResource = RenderGraph.ResourceMap.TrySetProperties(renderPassDataHandle.Item1, RenderGraph.FrameIndex, this, command);
            Assert.IsTrue(hasResource || renderPassDataHandle.Item2);
        }

        ExecuteRenderPassBuilder();

        readTextures.Clear();
        readBuffers.Clear();
        writeBuffers.Clear();

        Execute();

        if (UseProfiler)
            Command.EndSample(Name);
    }

    public virtual void PostExecute() { }

    public override string ToString() => Name;

    public void ReadTexture(int propertyId, ResourceHandle<RenderTexture> texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
    {
        Assert.IsFalse(RenderGraph.IsExecuting);
        readTextures.Add((propertyId, texture, mip, subElement));
        RenderGraph.RtHandleSystem.ReadResource(texture, Index);
    }

    public void ReadTexture(string propertyName, ResourceHandle<RenderTexture> texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
    {
        ReadTexture(Shader.PropertyToID(propertyName), texture, mip, subElement);
    }

    public void ReadBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer, int size = 0, int offset = 0)
    {
        RenderGraph.BufferHandleSystem.ReadResource(buffer, Index);
        readBuffers.Add((propertyName, buffer, size, offset));
    }

    public void WriteBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
    {
        RenderGraph.BufferHandleSystem.WriteResource(buffer, Index);
        writeBuffers.Add((propertyName, buffer));
    }

    public bool TryReadResource(Type type)
    {
        Assert.IsFalse(RenderGraph.IsExecuting);
        var handle = RenderGraph.ResourceMap.GetResourceHandle(type);
        var hasResource = RenderGraph.ResourceMap.TrySetInputs(handle, RenderGraph.FrameIndex, this);

        if (hasResource)
            RenderPassDataHandles.Add((handle, false));

        return hasResource;
    }

    public void ReadResource(Type type, bool isOptional = false)
    {
        Assert.IsFalse(RenderGraph.IsExecuting);
        var handle = RenderGraph.ResourceMap.GetResourceHandle(type);
        var hasResource = RenderGraph.ResourceMap.TrySetInputs(handle, RenderGraph.FrameIndex, this);

        if (!isOptional && !hasResource)
            throw new InvalidOperationException($"Non-optional resource of type {type} does not exist");

        RenderPassDataHandles.Add((handle, isOptional));
    }

    public void ReadResource<T>(bool isOptional = false) where T : struct, IRenderPassData
    {
        ReadResource(typeof(T), isOptional);
    }

    public bool TryReadResource<T>() where T : struct, IRenderPassData
    {
        return TryReadResource(typeof(T));
    }
}