using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class PersistentRTHandleCache : IDisposable
{
	private readonly Dictionary<Camera, ResourceHandle<RenderTexture>> textureCache = new();

	private readonly GraphicsFormat format;
	private readonly TextureDimension dimension;

	private readonly bool hasMips;
	private readonly string name;

	public RenderGraph renderGraph;
	private bool disposedValue;
	private readonly bool isScreenTexture;
	private readonly bool autoGenerateMips;
	private readonly RTClearFlags clearFlags;
	private readonly float clearDepth;
	private readonly uint clearStencil;
	private readonly Color clearColor;

	public PersistentRTHandleCache(GraphicsFormat format, RenderGraph renderGraph, string name = "", TextureDimension dimension = TextureDimension.Tex2D, bool hasMips = false, bool isScreenTexture = false, bool autoGenerateMips = false, RTClearFlags clearFlags = RTClearFlags.None, Color clearColor = default, float clearDepth = 1f, uint clearStencil = 0u)
	{
		this.format = format;
		this.dimension = dimension;
		this.renderGraph = renderGraph;
		this.name = name;
		this.hasMips = hasMips;
		this.isScreenTexture = isScreenTexture;
		this.autoGenerateMips = autoGenerateMips;
		this.clearFlags = clearFlags;
		this.clearColor = clearColor;
		this.clearDepth = clearDepth;
		this.clearStencil = clearStencil;
	}

	// Gets current texture and marks history as non-persistent
	public (ResourceHandle<RenderTexture> current, ResourceHandle<RenderTexture> history, bool wasCreated) GetTextures(int width, int height, Camera camera, int depth = 1)
	{
		var wasCreated = !textureCache.TryGetValue(camera, out var history);
		if (wasCreated)
		{
			switch (dimension)
			{
				case TextureDimension.Tex2D:
					history = renderGraph.EmptyTexture;
					break;
				case TextureDimension.Tex3D:
					history = renderGraph.Empty3DTexture;
					break;
				case TextureDimension.Cube:
					history = renderGraph.EmptyCubemap;
					break;
				case TextureDimension.Tex2DArray:
					history = renderGraph.EmptyTextureArray;
					break;
				case TextureDimension.CubeArray:
					history = renderGraph.EmptyCubemapArray;
					break;
				default:
					throw new NotSupportedException(dimension.ToString());
			}
		}
		else
			renderGraph.ReleasePersistentResource(history);

		var current = renderGraph.GetTexture(width, height, format, depth, dimension, isScreenTexture, hasMips, autoGenerateMips, true, false, false, clearFlags, clearColor, clearDepth, clearStencil);
		textureCache[camera] = current;

		return (current, history, wasCreated);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposedValue)
			return;

		foreach (var texture in textureCache)
			renderGraph.ReleasePersistentResource(texture.Value);

		if (!disposing)
			Debug.LogError($"Persistent RT Handle Cache [{name}] not disposed correctly");

		disposedValue = true;
	}

	~PersistentRTHandleCache()
	{
		Dispose(false);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}