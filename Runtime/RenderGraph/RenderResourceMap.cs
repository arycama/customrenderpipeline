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
	private readonly List<ResourceMapEntry> handleList = new();
	private bool disposedValue;

	public RenderPassDataHandle GetResourceHandle(Type type)
	{
		if (!handleIndexMap.TryGetValue(type, out var handle))
		{
			handle = new(handleIndexMap.Count, type);
			handleIndexMap.Add(type, handle);
			handleList.Add((null, 0, false, false));
		}

		return handle;
	}

	public RenderPassDataHandle GetResourceHandle<T>() where T : struct, IRenderPassData
	{
		return GetResourceHandle(typeof(T));
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

		ResourceMapData<T> mapData;
		if (!data.hasData)
		{
			mapData = new ResourceMapData<T>();
			data.data = mapData;
			data.hasData = true;
		}
		else
			mapData = data.data as ResourceMapData<T>;

		mapData.resource = renderResource;
		data.frameIndex = frameIndex;
		data.isPersistent = isPersistent;
		handleList[handle.Index] = data;
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

public struct ResourceMapEntry
{
	public ResourceMapData data;
	public int frameIndex;
	public bool isPersistent;
	public bool hasData;

	public ResourceMapEntry(ResourceMapData data, int frameIndex, bool isPersistent, bool hasData)
	{
		this.data = data;
		this.frameIndex = frameIndex;
		this.isPersistent = isPersistent;
		this.hasData = hasData;
	}

	public override bool Equals(object obj) => obj is ResourceMapEntry other && EqualityComparer<ResourceMapData>.Default.Equals(data, other.data) && frameIndex == other.frameIndex && isPersistent == other.isPersistent && hasData == other.hasData;
	public override int GetHashCode() => HashCode.Combine(data, frameIndex, isPersistent, hasData);

	public void Deconstruct(out ResourceMapData data, out int frameIndex, out bool isPersistent, out bool hasData)
	{
		data = this.data;
		frameIndex = this.frameIndex;
		isPersistent = this.isPersistent;
		hasData = this.hasData;
	}

	public static implicit operator (ResourceMapData data, int frameIndex, bool isPersistent, bool hasData)(ResourceMapEntry value) => (value.data, value.frameIndex, value.isPersistent, value.hasData);
	public static implicit operator ResourceMapEntry((ResourceMapData data, int frameIndex, bool isPersistent, bool hasData) value) => new ResourceMapEntry(value.data, value.frameIndex, value.isPersistent, value.hasData);
}