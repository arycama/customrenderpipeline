using System;
using UnityEngine;

/// <summary> Base class for render features</summary>
public abstract class RenderFeatureBase : IDisposable
{
	protected readonly RenderGraph renderGraph;
	private bool disposedValue;

	public virtual bool HasProfilerMarker => true;
	public virtual string ProfilerNameOverride => null;

	public RenderFeatureBase(RenderGraph renderGraph) 
	{
		this.renderGraph = renderGraph;
	}

	~RenderFeatureBase()
	{
		Dispose(disposing: false);
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected void Dispose(bool disposing)
	{
		if (disposedValue)
			return;

		if (!disposing)
			Debug.LogError($"Render Feature [{GetType()}] not disposed correctly");

		try
		{
			Cleanup(disposing);
		}
		finally
		{
			disposedValue = true;
		}
	}

	/// <summary> Override in derived classes and put any cleanup code here (Eg free buffers, RT handles etc) </summary>
	protected virtual void Cleanup(bool disposing) { }
}
