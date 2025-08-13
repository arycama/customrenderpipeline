using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

public class RenderResourceMap : IDisposable
{
	private readonly Dictionary<Type, RenderPassDataHandle> handleIndexMap = new();
	private readonly List<(IRenderPassData data, int frameIndex, bool isPersistent)> handleList = new();
	private bool disposedValue;
	private readonly RenderGraph renderGraph;

	public RenderResourceMap(RenderGraph renderGraph)
	{
		this.renderGraph = renderGraph;
	}

	// TODO: Should have a tryget method which fails if not already initialized? (Thoguh we should keep this one so that types can prefetch handles out of order
	public RenderPassDataHandle GetResourceHandle<T>() where T : IRenderPassData
	{
		if (!handleIndexMap.TryGetValue(typeof(T), out var handle))
		{
			handle = new(handleIndexMap.Count, typeof(T));
			handleIndexMap.Add(typeof(T), handle);
			handleList.Add((null, 0, false));
		}

		return handle;
	}

	public T GetRenderPassData<T>(RenderPassDataHandle handle, int frameIndex) where T : IRenderPassData
	{
		var result = handleList[handle.Index];

		if (result.data == null)
			throw new InvalidOperationException($"Data has not been set for type {typeof(T)}");

		//Assert.IsNotNull(result.Item1, "Data has not been set for type");
		Assert.IsTrue(result.isPersistent || result.frameIndex == frameIndex, "Getting non-persistent renderdata for a previous frame");
		return (T)result.data;
	}

	public bool TryGetRenderPassData<T>(RenderPassDataHandle handle, int frameIndex, out T data) where T : IRenderPassData
	{
		var result = handleList[handle.Index];

		if ((result.isPersistent || frameIndex == result.frameIndex) && result.data != null)
		{
			data = (T)result.data;
			return true;
		}

		data = default;
		return false;
	}

	public bool TryGetRenderPassData<T>(int frameIndex, out T data) where T : IRenderPassData
	{
		var handle = GetResourceHandle<T>();
		return TryGetRenderPassData(handle, frameIndex, out data);
	}

	public bool IsRenderPassDataValid<T>(RenderPassDataHandle handle, int frameIndex) where T : IRenderPassData
	{
		var result = handleList[handle.Index];

		if (frameIndex == result.frameIndex && result.data != null)
		{
			return true;
		}

		return false;
	}

	public bool IsRenderPassDataValid<T>(int frameIndex) where T : IRenderPassData
	{
		var handle = GetResourceHandle<T>();
		var result = handleList[handle.Index];

		if (frameIndex == result.frameIndex && result.data != null)
		{
			return true;
		}

		return false;
	}

	public T GetRenderPassData<T>(int frameIndex) where T : IRenderPassData
	{ 
		var handle = GetResourceHandle<T>();
		return GetRenderPassData<T>(handle, frameIndex);
	}

	public void SetRenderPassData(RenderPassDataHandle handle, IRenderPassData renderResource, int frameIndex, bool isPersistent = false)
	{
		Assert.IsFalse(renderGraph.IsExecuting);
		handleList[handle.Index] = (renderResource, frameIndex, isPersistent);
	}

	public void SetRenderPassData<T>(in T renderResource, int frameIndex, bool isPersistent = false) where T : IRenderPassData
	{
		var handle = GetResourceHandle<T>();
		SetRenderPassData(handle, renderResource, frameIndex, isPersistent);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposedValue)
			return;

		if (disposing)
		{
			handleIndexMap.Clear();
			handleList.Clear();
		}
		else
			Debug.LogError("Render Resource Map not disposed correctly");

		disposedValue = true;
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
}