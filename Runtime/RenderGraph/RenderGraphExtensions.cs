using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public static class RenderGraphExtensions
{
    public static BlitToScreenPass<T> AddBlitToScreenPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<BlitToScreenPass<T>, T>(name, data);
    public static ComputeRenderPass<T> AddComputeRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<ComputeRenderPass<T>, T>(name, data);
    public static DrawInstancedIndirectRenderPass<T> AddDrawInstancedIndirectRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawInstancedIndirectRenderPass<T>, T>(name, data);
    public static DrawProceduralIndexedRenderPass<T> AddDrawProceduralIndexedRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawProceduralIndexedRenderPass<T>, T>(name, data);
    public static DrawProceduralIndirectIndexedRenderPass<T> AddDrawProceduralIndirectIndexedRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawProceduralIndirectIndexedRenderPass<T>, T>(name, data);
    public static DrawProceduralIndirectRenderPass<T> AddDrawProceduralIndirectRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass<T>, T>(name, data);
    public static DrawProceduralRenderPass<T> AddDrawProceduralRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<DrawProceduralRenderPass<T>, T>(name, data);
    public static FullscreenRenderPass<T> AddFullscreenRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<FullscreenRenderPass<T>, T>(name, data);
    public static GenericRenderPass<T> AddGenericRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<GenericRenderPass<T>, T>(name, data);
    public static IndirectComputeRenderPass<T> AddIndirectComputeRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<IndirectComputeRenderPass<T>, T>(name, data);
    public static ObjectRenderPass<T> AddObjectRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<ObjectRenderPass<T>, T>(name, data);
    public static RaytracingRenderPass<T> AddRaytracingRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<RaytracingRenderPass<T>, T>(name, data);
    public static ShadowRenderPass<T> AddShadowRenderPass<T>(this RenderGraph renderGraph, string name, T data) => renderGraph.AddRenderPass<ShadowRenderPass<T>, T>(name, data);

    // Defaults (Still need a type to avoid unneccessary class variants)
    public static BlitToScreenPass<int> AddBlitToScreenPass(this RenderGraph renderGraph, string name) => AddBlitToScreenPass(renderGraph, name, 0);
    public static ComputeRenderPass<int> AddComputeRenderPass(this RenderGraph renderGraph, string name) => AddComputeRenderPass(renderGraph, name, 0);
    public static DrawInstancedIndirectRenderPass<int> AddDrawInstancedIndirectRenderPass(this RenderGraph renderGraph, string name) => AddDrawInstancedIndirectRenderPass(renderGraph, name, 0);
    public static DrawProceduralIndexedRenderPass<int> AddDrawProceduralIndexedRenderPass(this RenderGraph renderGraph, string name) => AddDrawProceduralIndexedRenderPass(renderGraph, name, 0);
    public static DrawProceduralIndirectIndexedRenderPass<int> AddDrawProceduralIndirectIndexedRenderPass(this RenderGraph renderGraph, string name) => AddDrawProceduralIndirectIndexedRenderPass(renderGraph, name, 0);
    public static DrawProceduralIndirectRenderPass<int> AddDrawProceduralIndirectRenderPass(this RenderGraph renderGraph, string name) => AddDrawProceduralIndirectRenderPass(renderGraph, name, 0);
    public static DrawProceduralRenderPass<int> AddDrawProceduralRenderPass(this RenderGraph renderGraph, string name) => AddDrawProceduralRenderPass(renderGraph, name, 0);
    public static FullscreenRenderPass<int> AddFullscreenRenderPass(this RenderGraph renderGraph, string name) => AddFullscreenRenderPass(renderGraph, name, 0);
    public static GenericRenderPass<int> AddGenericRenderPass(this RenderGraph renderGraph, string name) => AddGenericRenderPass(renderGraph, name, 0);
    public static IndirectComputeRenderPass<int> AddIndirectComputeRenderPass(this RenderGraph renderGraph, string name) => AddIndirectComputeRenderPass(renderGraph, name, 0);
    public static ObjectRenderPass<int> AddObjectRenderPass(this RenderGraph renderGraph, string name) => AddObjectRenderPass(renderGraph, name, 0);
    public static RaytracingRenderPass<int> AddRaytracingRenderPass(this RenderGraph renderGraph, string name) => AddRaytracingRenderPass(renderGraph, name, 0);
    public static ShadowRenderPass<int> AddShadowRenderPass(this RenderGraph renderGraph, string name) => AddShadowRenderPass(renderGraph, name, 0);

    public static void AddProfileBeginPass(this RenderGraph renderGraph, string name)
    {
        // TODO: There might be a more concise way to do this
        var pass = renderGraph.AddGenericRenderPass(name);
        pass.UseProfiler = false;

        pass.SetRenderFunction(static (command, pass) =>
        {
            command.BeginSample(pass.Name);
        });
    }

    public static void AddProfileEndPass(this RenderGraph renderGraph, string name)
    {
        // TODO: There might be a more concise way to do this
        var pass = renderGraph.AddGenericRenderPass(name);
        pass.UseProfiler = false;

        pass.SetRenderFunction(static (command, pass) =>
        {
            command.EndSample(pass.Name);
        });
    }

    public static ProfilePassScope AddProfileScope(this RenderGraph renderGraph, string name) => new(name, renderGraph);

    public static ResourceHandle<RenderTexture> GetTexture(this RenderGraph renderGraph, RtHandleDescriptor descriptor, bool isPersistent = false)
    {
        Assert.IsFalse(renderGraph.IsExecuting);
        return renderGraph.RtHandleSystem.GetResourceHandle(descriptor, isPersistent);
    }

    /// <summary> Gets a texture with the same attributes as the handle </summary>
    public static ResourceHandle<RenderTexture> GetTexture(this RenderGraph renderGraph, ResourceHandle<RenderTexture> handle, bool isPersistent = false)
    {
        var descriptor = renderGraph.RtHandleSystem.GetDescriptor(handle);
        return renderGraph.RtHandleSystem.GetResourceHandle(descriptor, isPersistent);
    }

    public static ResourceHandle<RenderTexture> GetTexture(this RenderGraph renderGraph, Int2 size, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false, bool isExactSize = false, bool isRandomWrite = false, bool clear = false, Color clearColor = default, float clearDepth = 1f, uint clearStencil = 0u, VRTextureUsage vrTextureUsage = VRTextureUsage.None, int antiAliasing = 1)
    {
        return renderGraph.GetTexture(new RtHandleDescriptor(size.x, size.y, format, volumeDepth, dimension, isScreenTexture, hasMips, autoGenerateMips, isRandomWrite, isExactSize, clear, clearColor, clearDepth, clearStencil, vrTextureUsage, antiAliasing), isPersistent);
    }

    public static ResourceHandle<GraphicsBuffer> GetBuffer(this RenderGraph renderGraph, int count = 1, int stride = sizeof(int), GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None, bool isPersistent = false)
    {
        Assert.IsFalse(renderGraph.IsExecuting);
        return renderGraph.BufferHandleSystem.GetResourceHandle(new BufferHandleDescriptor(count, stride, target, usageFlags), isPersistent);
    }

    public static void SetResource<T>(this RenderGraph renderGraph, T resource, bool isPersistent = false) where T : struct, IRenderPassData
    {
        Assert.IsFalse(renderGraph.IsExecuting);
        renderGraph.ResourceMap.SetRenderPassData(resource, renderGraph.FrameIndex, isPersistent);
    }

    public static void ClearResource<T>(this RenderGraph renderGraph) where T : struct, IRenderPassData
    {
        Assert.IsFalse(renderGraph.IsExecuting);
        renderGraph.ResourceMap.SetRenderPassData<T>(default, -1, false);
    }

    public static bool TryGetResource<T>(this RenderGraph renderGraph, out T resource) where T : struct, IRenderPassData
    {
        Assert.IsFalse(renderGraph.IsExecuting);
        return renderGraph.ResourceMap.TryGetResource<T>(renderGraph.FrameIndex, out resource);
    }

    public static T GetResource<T>(this RenderGraph renderGraph) where T : struct, IRenderPassData
    {
        var hasResource = renderGraph.TryGetResource<T>(out var resource);
        Assert.IsTrue(hasResource);
        return resource;
    }

    public static ResourceHandle<RenderTexture> GetRTHandle<T>(this RenderGraph renderGraph) where T : IRtHandleId
    {
        return renderGraph.GetRTHandle(typeof(T)).handle;
    }

    public static ResourceHandle<GraphicsBuffer> SetConstantBuffer<T>(this RenderGraph renderGraph, T data) where T : unmanaged
    {
        Assert.IsFalse(renderGraph.IsExecuting);
        Assert.AreEqual(0, UnsafeUtility.SizeOf<T>() % 4, "ConstantBuffer size must be a multiple of 4 bytes");

        var size = UnsafeUtility.SizeOf<T>();
        var buffer = renderGraph.GetBuffer(1, size, GraphicsBuffer.Target.Constant);

        using var pass = renderGraph.AddGenericRenderPass("Set Constant Buffer", (data, buffer, size));
        pass.WriteBuffer("", buffer);
        pass.SetRenderFunction(static (command, pass, data) =>
        {
            var array = new NativeArray<T>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            {
                array[0] = data.data;
            }

            command.SetBufferData(pass.GetBuffer(data.buffer), array);
        });

        return buffer;
    }

    public static void ReleasePersistentResource(this RenderGraph renderGraph, ResourceHandle<GraphicsBuffer> handle, int passIndex)
    {
        Assert.IsTrue(!renderGraph.IsExecuting || renderGraph.RenderPipeline.IsDisposing);
        renderGraph.BufferHandleSystem.ReleasePersistentResource(handle, passIndex);
    }

    public static void ReleasePersistentResource(this RenderGraph renderGraph, ResourceHandle<RenderTexture> handle, int passIndex)
    {
        Assert.IsTrue(!renderGraph.IsExecuting || renderGraph.RenderPipeline.IsDisposing);
        renderGraph.RtHandleSystem.ReleasePersistentResource(handle, passIndex);
    }

    public static Float4 GetScaleLimit2D(this RenderGraph renderGraph, ResourceHandle<RenderTexture> handle)
    {
        Assert.IsTrue(renderGraph.IsExecuting);

        var descriptor = renderGraph.RtHandleSystem.GetDescriptor(handle);
        var resource = renderGraph.RtHandleSystem.GetResource(handle);

        var scaleX = (float)descriptor.width / resource.width;
        var scaleY = (float)descriptor.height / resource.height;
        var limitX = (descriptor.width - 0.5f) / resource.width;
        var limitY = (descriptor.height - 0.5f) / resource.height;

        return new Float4(scaleX, scaleY, limitX, limitY);
    }

    public static Float3 GetScale3D(this RenderGraph renderGraph, ResourceHandle<RenderTexture> handle)
    {
        Assert.IsTrue(renderGraph.IsExecuting);

        var descriptor = renderGraph.RtHandleSystem.GetDescriptor(handle);
        var resource = renderGraph.RtHandleSystem.GetResource(handle);

        var scaleX = (float)descriptor.width / resource.width;
        var scaleY = (float)descriptor.height / resource.height;
        var scaleZ = (float)descriptor.volumeDepth / resource.volumeDepth;

        return new Float3(scaleX, scaleY, scaleZ);
    }

    public static Float3 GetLimit3D(this RenderGraph renderGraph, ResourceHandle<RenderTexture> handle)
    {
        Assert.IsTrue(renderGraph.IsExecuting);

        var descriptor = renderGraph.RtHandleSystem.GetDescriptor(handle);
        var resource = renderGraph.RtHandleSystem.GetResource(handle);

        var limitX = (descriptor.width - 0.5f) / resource.width;
        var limitY = (descriptor.height - 0.5f) / resource.height;
        var limitZ = (descriptor.volumeDepth - 0.5f) / resource.volumeDepth;

        return new Float3(limitX, limitY, limitZ);
    }

    public static ResourceHandle<GraphicsBuffer> GetGridIndexBuffer(this RenderGraph renderGraph, int cellsPerRow, bool isQuad, bool alternateIndices)
    {
        var indexCount = cellsPerRow * cellsPerRow * (isQuad ? 4 : 6);
        var indexBuffer = renderGraph.GetBuffer(indexCount, sizeof(ushort), GraphicsBuffer.Target.Index, isPersistent: true);

        var indices = ListPool<ushort>.Get();
        GraphicsUtilities.GenerateGridIndexBuffer(indices, cellsPerRow, isQuad, alternateIndices);

        using (var pass = renderGraph.AddGenericRenderPass("Terrain Set Index Data", (indexBuffer, indices)))
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

    public static ResourceHandle<GraphicsBuffer> GetQuadIndexBuffer(this RenderGraph renderGraph, int count, bool isQuad)
    {
        var indexCount = count * (isQuad ? 4 : 6);

        var isShort = indexCount < ushort.MaxValue;
        var size = isShort ? sizeof(ushort) : sizeof(uint);
        var indexBuffer = renderGraph.GetBuffer(indexCount, size, GraphicsBuffer.Target.Index, isPersistent: true);

        if (isShort)
        {
            var indices = ListPool<ushort>.Get();
            GraphicsUtilities.GenerateQuadIndexBuffer(indices, count, isQuad);

            using (var pass = renderGraph.AddGenericRenderPass("Terrain Set Index Data", (indexBuffer, indices)))
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

            using (var pass = renderGraph.AddGenericRenderPass("Terrain Set Index Data", (indexBuffer, indices)))
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