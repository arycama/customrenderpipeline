using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public class RenderGraph : IDisposable
{
	private readonly Dictionary<Type, Stack<RenderPassBase>> renderPassPool = new();

	private bool disposedValue;
	private readonly List<RenderPassBase> renderPasses = new();

	private readonly GraphicsBuffer emptyBuffer;
	private readonly RenderTexture emptyTexture, emptyUavTexture, emptyTextureArray, empty3DTexture, emptyCubemap, emptyCubemapArray;

	public RTHandleSystem RtHandleSystem { get; }
	public BufferHandleSystem BufferHandleSystem { get; }
	public RenderResourceMap ResourceMap { get; }
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

		ResourceMap = new(this);
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
		disposedValue = true;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	public T AddRenderPass<T>(string name) where T : RenderPassBase, new()
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

	public void AddProfileBeginPass(string name)
	{
		// TODO: There might be a more concise way to do this
		var pass = AddRenderPass<GenericRenderPass>(name);
		pass.UseProfiler = false;

		pass.SetRenderFunction((command, pass) =>
		{
			command.BeginSample(name);
		});
	}

	public void AddProfileEndPass(string name)
	{
		// TODO: There might be a more concise way to do this
		var pass = AddRenderPass<GenericRenderPass>(name);
		pass.UseProfiler = false;

		pass.SetRenderFunction((command, pass) =>
		{
			command.EndSample(name);
		});
	}

	public ProfilePassScope AddProfileScope(string name) => new(name, this);

	public void Execute(CommandBuffer command)
	{
		BufferHandleSystem.AllocateFrameResources(renderPasses.Count, FrameIndex);
		RtHandleSystem.AllocateFrameResources(renderPasses.Count, FrameIndex);

		try
		{
			IsExecuting = true;

			foreach (var renderPass in renderPasses)
			{
				renderPass.Run(command);

				renderPass.Reset();

				if (!renderPassPool.TryGetValue(renderPass.GetType(), out var pool))
				{
					pool = new();
					renderPassPool.Add(renderPass.GetType(), pool);
				}

				pool.Push(renderPass);
			}
		}
		catch(Exception exception)
		{
			CleanupCurrentFrame();
			throw exception;
		}
		finally
		{
			IsExecuting = false;
		}
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

	public ResourceHandle<RenderTexture> GetTexture(int width, int height, GraphicsFormat format, int volumeDepth = 1, TextureDimension dimension = TextureDimension.Tex2D, bool isScreenTexture = false, bool hasMips = false, bool autoGenerateMips = false, bool isPersistent = false, bool isExactSize = false, bool isRandomWrite = false, RTClearFlags clearFlags = RTClearFlags.None, Color clearColor = default, float clearDepth = 1f, uint clearStencil = 0u)
	{
		return GetTexture(new RtHandleDescriptor(width, height, format, volumeDepth, dimension, isScreenTexture, hasMips, autoGenerateMips, isRandomWrite, isExactSize, clearFlags, clearColor, clearDepth, clearStencil), isPersistent);
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

		IsExecuting = false;
	}

	public void SetResource<T>(T resource, bool isPersistent = false) where T : IRenderPassData
	{
		Assert.IsFalse(IsExecuting);
		ResourceMap.SetRenderPassData(resource, FrameIndex, isPersistent);
	}

	public void ClearResource<T>() where T : IRenderPassData
	{
		Assert.IsFalse(IsExecuting);
		ResourceMap.SetRenderPassData<T>(default, -1, false);
	}

	public bool IsRenderPassDataValid<T>() where T : IRenderPassData
	{
		return ResourceMap.IsRenderPassDataValid<T>(FrameIndex);
	}

	public bool TryGetResource<T>(out T resource) where T : IRenderPassData
	{
		Assert.IsFalse(IsExecuting);
		return ResourceMap.TryGetRenderPassData<T>(FrameIndex, out resource);
	}

	public T GetResource<T>() where T : IRenderPassData
	{
		Assert.IsFalse(IsExecuting);
		return ResourceMap.GetRenderPassData<T>(FrameIndex);
	}

	public ResourceHandle<GraphicsBuffer> SetConstantBuffer<T>(T data) where T : struct
	{
		Assert.IsFalse(IsExecuting);
		Assert.AreEqual(0, UnsafeUtility.SizeOf<T>() % 4, "ConstantBuffer size must be a multiple of 4 bytes");

		//var buffer = BufferHandleSystem.GetResourceHandle(new BufferHandleDescriptor(1, UnsafeUtility.SizeOf<T>(), GraphicsBuffer.Target.Constant, GraphicsBuffer.UsageFlags.LockBufferForWrite));

		// TODO: Re-investigate if lock buffer for write is worth using. Currently it causes read/write hazards where current frame data can be overridden, causing rendering issues. May need to revise our buffer handling logic
		var buffer = BufferHandleSystem.GetResourceHandle(new BufferHandleDescriptor(1, UnsafeUtility.SizeOf<T>(), GraphicsBuffer.Target.Constant));

		using var pass = AddRenderPass<GenericRenderPass>("Set Constant Buffer");
		pass.WriteBuffer("", buffer);
		pass.SetRenderFunction((data, buffer), static (command, pass, data) =>
		{
			var array = new NativeArray<T>(1, Allocator.Temp);
			array[0] = data.data;
			command.SetBufferData(pass.GetBuffer(data.buffer), array);

			//using var bufferData = pass.GetBuffer(data.buffer).DirectWrite<T>();
			//bufferData.SetData(0, data.data);
		});

		return buffer;
	}

	public void ReleasePersistentResource(ResourceHandle<GraphicsBuffer> handle)
	{
		Assert.IsFalse(IsExecuting);
		BufferHandleSystem.ReleasePersistentResource(handle);
	}

	public void ReleasePersistentResource(ResourceHandle<RenderTexture> handle)
	{
		Assert.IsFalse(IsExecuting);
		RtHandleSystem.ReleasePersistentResource(handle);
	}
}