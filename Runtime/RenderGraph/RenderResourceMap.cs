using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public abstract class ResourceMapData
{
	public abstract void SetInputs(RenderPass pass);
	public abstract void SetProperties(RenderPass pass, CommandBuffer command);
}

public class ResourceMapData<T> : ResourceMapData where T : struct, IRenderPassData
{
	public T resource;

	public override void SetInputs(RenderPass pass)
	{
		resource.SetInputs(pass);
	}

	public override void SetProperties(RenderPass pass, CommandBuffer command)
	{
		resource.SetProperties(pass, command);
	}
}

public class RenderResourceMap : IDisposable
{
	private readonly Dictionary<Type, RenderPassDataHandle> handleIndexMap = new();
	private readonly List<(ResourceMapData data, int frameIndex, bool isPersistent, bool hasData)> handleList = new();
	private bool disposedValue;

	public RenderPassDataHandle GetResourceHandle<T>() where T : struct, IRenderPassData
	{
		if (!handleIndexMap.TryGetValue(typeof(T), out var handle))
		{
			handle = new(handleIndexMap.Count, typeof(T));
			handleIndexMap.Add(typeof(T), handle);
			handleList.Add((new ResourceMapData<T>(), 0, false, false));
		}

		return handle;
	}

	public bool TrySetProperties(RenderPassDataHandle handle, int frameIndex, RenderPass renderPass, CommandBuffer command)
	{
		var result = handleList[handle.Index];
		if ((result.isPersistent || frameIndex == result.frameIndex) && result.hasData)
		{
			result.data.SetProperties(renderPass, command);
			return true;
		}

		return false;
	}

	public bool TrySetInputs(RenderPassDataHandle handle, int frameIndex, RenderPass renderPass)
	{
		var result = handleList[handle.Index];
		if ((result.isPersistent || frameIndex == result.frameIndex) && result.hasData)
		{
			result.data.SetInputs(renderPass);
			return true;
		}

		return false;
	}

	public bool TryGetResource<T>(int frameIndex, out T data) where T : struct, IRenderPassData
	{
		var handle = GetResourceHandle<T>();
		var result = handleList[handle.Index];
		var mapData = result.data as ResourceMapData<T>;

		if ((result.isPersistent || frameIndex == result.frameIndex) && result.hasData)
		{
			data = mapData.resource;
			return true;
		}

		data = default;
		return false;
	}

	public void SetRenderPassData<T>(T renderResource, int frameIndex, bool isPersistent = false) where T : struct, IRenderPassData
	{
		var handle = GetResourceHandle<T>();
		var data = handleList[handle.Index];
		var mapData = data.data as ResourceMapData<T>;
		mapData.resource = renderResource;
		handleList[handle.Index] = (mapData, frameIndex, isPersistent, true);
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