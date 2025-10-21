using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public abstract class RenderPass : IDisposable
{
	// TODO: Convert to handles and remove
	protected readonly List<(int, ResourceHandle<RenderTexture>, int, RenderTextureSubElement)> readTextures = new();
	protected readonly List<(string, ResourceHandle<GraphicsBuffer>, int size, int offset)> readBuffers = new();
	protected readonly List<(string, ResourceHandle<GraphicsBuffer>)> writeBuffers = new();

	private readonly List<(RenderPassDataHandle, bool)> RenderPassDataHandles = new();
	private readonly List<Type> readRtHandles = new();

	private Action<CommandBuffer, RenderPass> defaultRenderFunction;
	private IRenderFunction renderGraphBuilder;
	private object renderData;

	protected CommandBuffer Command { get; private set; }
	public RenderGraph RenderGraph { get; set; }
	internal string Name { get; set; }
	internal int Index { get; set; }
	public bool UseProfiler { get; set; } = true;

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

	protected virtual void SetupTargets() { }

	protected void ExecuteRenderPassBuilder()
	{
		if (defaultRenderFunction != null)
			defaultRenderFunction(Command, this);
		else
			renderGraphBuilder?.Execute(Command, this, renderData);
	}

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
		readRtHandles.Clear();
		defaultRenderFunction = null;
		renderGraphBuilder = null;
		renderData = null;
	}

	void IDisposable.Dispose()
	{
		// TODO: Should anything be done here?
	}

	public GraphicsBuffer GetBuffer(ResourceHandle<GraphicsBuffer> handle)
	{
		Assert.IsTrue(RenderGraph.IsExecuting);
		return RenderGraph.BufferHandleSystem.GetResource(handle);
	}

	public RenderTexture GetRenderTexture(ResourceHandle<RenderTexture> handle)
	{
		Assert.IsTrue(RenderGraph.IsExecuting);
		return RenderGraph.RtHandleSystem.GetResource(handle);
	}

	public virtual void PreExecute()
	{
	}

	public void ReadRtHandle<T>()
	{
		readRtHandles.Add(typeof(T));
		var handleData = RenderGraph.GetRTHandle(typeof(T));
		handleData.SetInputs(this);
	}

	public void Run(CommandBuffer command)
	{
		Command = command;

		if(UseProfiler)
			Command.BeginSample(Name);

		PreExecute();

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

		SetupTargets();

		// Set any data from each pass
		foreach (var renderPassDataHandle in RenderPassDataHandles)
		{
			if (renderPassDataHandle.Item2)
			{
				if (RenderGraph.ResourceMap.TryGetRenderPassData<IRenderPassData>(renderPassDataHandle.Item1, RenderGraph.FrameIndex, out var data))
					data.SetProperties(this, Command);
			}
			else
			{
				var data = RenderGraph.ResourceMap.GetRenderPassData<IRenderPassData>(renderPassDataHandle.Item1, RenderGraph.FrameIndex);
				data.SetProperties(this, Command);
			}
		}

		foreach(var handle in readRtHandles)
		{
			var handleData = RenderGraph.GetRTHandle(handle);
			handleData.SetProperties(this, Command);
		}

		ExecuteRenderPassBuilder();

		readTextures.Clear();
		readBuffers.Clear();
		writeBuffers.Clear();

		Execute();
		PostExecute();

		if(UseProfiler)
			Command.EndSample(Name);
	}

	protected virtual void PostExecute() { }

	public override string ToString() => Name;

	public void SetTexture(string propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		SetTexture(Shader.PropertyToID(propertyName), texture, mip, subElement);
	}

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

	public void AddRenderPassData<T>(bool isOptional = false) where T : struct, IRenderPassData
	{
		Assert.IsFalse(RenderGraph.IsExecuting);
		var handle = RenderGraph.ResourceMap.GetResourceHandle<T>();

		if (isOptional)
		{
			if (RenderGraph.ResourceMap.TryGetRenderPassData<T>(handle, RenderGraph.FrameIndex, out var data))
				data.SetInputs(this);
		}
		else
		{
			var data = RenderGraph.ResourceMap.GetRenderPassData<T>(handle, RenderGraph.FrameIndex);
			data.SetInputs(this);
		}

		RenderPassDataHandles.Add((handle, isOptional));
	}

	public Float4 GetScaleLimit2D(ResourceHandle<RenderTexture> handle)
	{
		var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
		var resource = GetRenderTexture(handle);

		var scaleX = (float)descriptor.width / resource.width;
		var scaleY = (float)descriptor.height / resource.height;
		var limitX = (descriptor.width - 0.5f) / resource.width;
		var limitY = (descriptor.height - 0.5f) / resource.height;

		return new Float4(scaleX, scaleY, limitX, limitY);
	}

	public Float3 GetScale3D(ResourceHandle<RenderTexture> handle)
	{
		var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
		var resource = GetRenderTexture(handle);

		var scaleX = (float)descriptor.width / resource.width;
		var scaleY = (float)descriptor.height / resource.height;
		var scaleZ = (float)descriptor.volumeDepth / resource.volumeDepth;

		return new Float3(scaleX, scaleY, scaleZ);
	}

	public Float3 GetLimit3D(ResourceHandle<RenderTexture> handle)
	{
		var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(handle);
		var resource = GetRenderTexture(handle);

		var limitX = (descriptor.width - 0.5f) / resource.width;
		var limitY = (descriptor.height - 0.5f) / resource.height;
		var limitZ = (descriptor.volumeDepth - 0.5f) / resource.volumeDepth;

		return new Float3(limitX, limitY, limitZ);
	}

	public void SetRenderFunction(Action<CommandBuffer, RenderPass> renderFunction)
	{
		defaultRenderFunction = renderFunction;
	}

	public void SetRenderFunction<K>(K data, Action<CommandBuffer, RenderPass, K> renderFunction)
	{
		renderData = data;
		renderGraphBuilder = new RenderFunction<K>(renderFunction);
	}
}
