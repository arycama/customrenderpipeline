using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RenderGraph : IDisposable
{
    private readonly Dictionary<Type, Stack<RenderPass>> renderPassPool = new();

    private bool disposedValue;
    private readonly List<RenderPass> renderPasses = new();

    private readonly GraphicsBuffer emptyBuffer;
    private readonly RenderTexture emptyTexture, emptyUavTexture, emptyTextureArray, empty3DTexture, emptyCubemap, emptyCubemapArray;
    private readonly Dictionary<Type, RTHandleData> rtHandles = new();
    private readonly NativeRenderPassSystem nativeRenderPassSystem = new();

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
    public bool IsDisposing { get; private set; }

    public bool DebugRenderPasses { get; set; }

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

        IsDisposing = true;

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
        return (T)result;
    }

    public T AddRenderPass<T, K>(string name, K data) where T : RenderPass<K>, new()
    {
        var result = AddRenderPass<T>(name);
        result.renderData = data;
        return result;
    }

    public void AddProfileBeginPass(string name)
    {
        // TODO: There might be a more concise way to do this
        var pass = this.AddGenericRenderPass(name);
        pass.UseProfiler = false;

        pass.SetRenderFunction(static (command, pass) =>
        {
            command.BeginSample(pass.Name);
        });
    }

    public void AddProfileEndPass(string name)
    {
        // TODO: There might be a more concise way to do this
        var pass = this.AddGenericRenderPass(name);
        pass.UseProfiler = false;

        pass.SetRenderFunction(static (command, pass) =>
        {
            command.EndSample(pass.Name);
        });
    }

    public ProfilePassScope AddProfileScope(string name) => new(name, this);

    public void Execute(CommandBuffer command)
    {
        BufferHandleSystem.AllocateFrameResources(renderPasses.Count, FrameIndex);
        RtHandleSystem.AllocateFrameResources(renderPasses.Count, FrameIndex);

        IsExecuting = true;

        nativeRenderPassSystem.CreateNativeRenderPasses(renderPasses);

        foreach (var renderPass in renderPasses)
        {
            //renderPass.SetupRenderPassData();
            renderPass.Run(command);

            // Re-add the pass to the pool
            if (!renderPassPool.TryGetValue(renderPass.GetType(), out var pool))
            {
                pool = new();
                renderPassPool.Add(renderPass.GetType(), pool);
            }

            pool.Push(renderPass);
        }

        IsExecuting = false;
    }

    public void BeginNativeRenderPass(int index, CommandBuffer command)
    {
        nativeRenderPassSystem.BeginNativeRenderPass(index, command);
    }

    public ResourceHandle<RenderTexture> GetTexture(RtHandleDescriptor descriptor, bool isPersistent = false)
    {
        Assert.IsFalse(IsExecuting);
        return RtHandleSystem.GetResourceHandle(descriptor, isPersistent);
    }

    /// <summary> Gets a texture with the same attributes as the handle </summary>
    public ResourceHandle<RenderTexture> GetTexture(ResourceHandle<RenderTexture> handle, bool isPersistent = false)
    {
        var descriptor = RtHandleSystem.GetDescriptor(handle);
        return RtHandleSystem.GetResourceHandle(descriptor, isPersistent);
    }

    public ResourceHandle<RenderTexture> GetTexture(Int2 size, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false, bool isExactSize = false, bool isRandomWrite = false, bool clear = false, Color clearColor = default, float clearDepth = 1f, uint clearStencil = 0u, VRTextureUsage vrTextureUsage = VRTextureUsage.None, bool isTransient = false)
    {
        return GetTexture(new RtHandleDescriptor(size.x, size.y, format, volumeDepth, dimension, isScreenTexture, hasMips, autoGenerateMips, isRandomWrite, isExactSize, clear, clearColor, clearDepth, clearStencil, vrTextureUsage, isTransient), isPersistent);
    }

    public ResourceHandle<GraphicsBuffer> GetBuffer(int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None, bool isPersistent = false)
    {
        Assert.IsFalse(IsExecuting);
        return BufferHandleSystem.GetResourceHandle(new BufferHandleDescriptor(count, stride, target, usageFlags), isPersistent);
    }

    public void CleanupCurrentFrame()
    {
        renderPasses.Clear();
        BufferHandleSystem.CleanupCurrentFrame(FrameIndex);
        RtHandleSystem.CleanupCurrentFrame(FrameIndex);

        if (!FrameDebugger.enabled)
            FrameIndex++;
    }

    public void SetResource<T>(T resource, bool isPersistent = false) where T : struct, IRenderPassData
    {
        Assert.IsFalse(IsExecuting);
        ResourceMap.SetRenderPassData(resource, FrameIndex, isPersistent);
    }

    public void ClearResource<T>() where T : struct, IRenderPassData
    {
        Assert.IsFalse(IsExecuting);
        ResourceMap.SetRenderPassData<T>(default, -1, false);
    }

    public bool TryGetResource<T>(out T resource) where T : struct, IRenderPassData
    {
        Assert.IsFalse(IsExecuting);
        return ResourceMap.TryGetResource<T>(FrameIndex, out resource);
    }

    public T GetResource<T>() where T : struct, IRenderPassData
    {
        var hasResource = TryGetResource<T>(out var resource);
        Assert.IsTrue(hasResource);
        return resource;
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

    public ResourceHandle<RenderTexture> GetRTHandle<T>() where T : IRtHandleId
    {
        return GetRTHandle(typeof(T)).handle;
    }

    public unsafe ResourceHandle<GraphicsBuffer> SetConstantBuffer<T>(T data) where T : unmanaged
    {
        Assert.IsFalse(IsExecuting);
        Assert.AreEqual(0, UnsafeUtility.SizeOf<T>() % 4, "ConstantBuffer size must be a multiple of 4 bytes");

        var size = UnsafeUtility.SizeOf<T>();
        var buffer = GetBuffer(1, size, GraphicsBuffer.Target.Constant);

        using var pass = this.AddGenericRenderPass("Set Constant Buffer", (data, buffer, size));
        pass.WriteBuffer("", buffer);
        pass.SetRenderFunction(static (command, pass, data) =>
        {
            var array = ArrayPool<byte>.Get(data.size);

            fixed (byte* arrayPtr = array)
            {
                byte* sourcePtr = (byte*)&data.data;
                Buffer.MemoryCopy(sourcePtr, arrayPtr, data.size, data.size);
            }

            command.SetBufferData(pass.GetBuffer(data.buffer), array);
            ArrayPool<byte>.Release(array);
        });

        return buffer;
    }

    public void ReleasePersistentResource(ResourceHandle<GraphicsBuffer> handle, int passIndex)
    {
        Assert.IsTrue(!IsExecuting || IsDisposing);
        BufferHandleSystem.ReleasePersistentResource(handle, passIndex);
    }

    public void ReleasePersistentResource(ResourceHandle<RenderTexture> handle, int passIndex)
    {
        Assert.IsTrue(!IsExecuting || IsDisposing);
        RtHandleSystem.ReleasePersistentResource(handle, passIndex);
    }

    public Float4 GetScaleLimit2D(ResourceHandle<RenderTexture> handle)
    {
        Assert.IsTrue(IsExecuting);

        var descriptor = RtHandleSystem.GetDescriptor(handle);
        var resource = RtHandleSystem.GetResource(handle);

        var scaleX = (float)descriptor.width / resource.width;
        var scaleY = (float)descriptor.height / resource.height;
        var limitX = (descriptor.width - 0.5f) / resource.width;
        var limitY = (descriptor.height - 0.5f) / resource.height;

        return new Float4(scaleX, scaleY, limitX, limitY);
    }

    public Float3 GetScale3D(ResourceHandle<RenderTexture> handle)
    {
        Assert.IsTrue(IsExecuting);

        var descriptor = RtHandleSystem.GetDescriptor(handle);
        var resource = RtHandleSystem.GetResource(handle);

        var scaleX = (float)descriptor.width / resource.width;
        var scaleY = (float)descriptor.height / resource.height;
        var scaleZ = (float)descriptor.volumeDepth / resource.volumeDepth;

        return new Float3(scaleX, scaleY, scaleZ);
    }

    public Float3 GetLimit3D(ResourceHandle<RenderTexture> handle)
    {
        Assert.IsTrue(IsExecuting);

        var descriptor = RtHandleSystem.GetDescriptor(handle);
        var resource = RtHandleSystem.GetResource(handle);

        var limitX = (descriptor.width - 0.5f) / resource.width;
        var limitY = (descriptor.height - 0.5f) / resource.height;
        var limitZ = (descriptor.volumeDepth - 0.5f) / resource.volumeDepth;

        return new Float3(limitX, limitY, limitZ);
    }

    public ResourceHandle<GraphicsBuffer> GetGridIndexBuffer(int cellsPerRow, bool isQuad, bool alternateIndices)
    {
        var indexCount = cellsPerRow * cellsPerRow * (isQuad ? 4 : 6);
        var indexBuffer = GetBuffer(indexCount, sizeof(ushort), GraphicsBuffer.Target.Index, isPersistent: true);

        var indices = ListPool<ushort>.Get();
        GraphicsUtilities.GenerateGridIndexBuffer(indices, cellsPerRow, isQuad, alternateIndices);

        using (var pass = this.AddGenericRenderPass("Terrain Set Index Data", (indexBuffer, indices)))
        {
            pass.WriteBuffer("", indexBuffer);
            pass.SetRenderFunction(static (command, pass, data) =>
            {
                command.SetBufferData(pass.GetBuffer(data.indexBuffer), data.indices);
                ListPool<ushort>.Release(data.indices);
            });
        }

        return indexBuffer;
    }

    public ResourceHandle<GraphicsBuffer> GetQuadIndexBuffer(int count, bool isQuad)
    {
        var indexCount = count * (isQuad ? 4 : 6);

        var isShort = indexCount < ushort.MaxValue;
        var size = isShort ? sizeof(ushort) : sizeof(uint);
        var indexBuffer = GetBuffer(indexCount, size, GraphicsBuffer.Target.Index, isPersistent: true);

        if (isShort)
        {
            var indices = ListPool<ushort>.Get();
            GraphicsUtilities.GenerateQuadIndexBuffer(indices, count, isQuad);

            using (var pass = this.AddGenericRenderPass("Terrain Set Index Data", (indexBuffer, indices)))
            {
                pass.WriteBuffer("", indexBuffer);
                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    command.SetBufferData(pass.GetBuffer(data.indexBuffer), data.indices);
                    ListPool<ushort>.Release(data.indices);
                });
            }
        }
        else
        {
            var indices = ListPool<uint>.Get();
            GraphicsUtilities.GenerateQuadIndexBuffer(indices, count, isQuad);

            using (var pass = this.AddGenericRenderPass("Terrain Set Index Data", (indexBuffer, indices)))
            {
                pass.WriteBuffer("", indexBuffer);
                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    command.SetBufferData(pass.GetBuffer(data.indexBuffer), data.indices);
                    ListPool<uint>.Release(data.indices);
                });
            }
        }

        return indexBuffer;
    }
}
