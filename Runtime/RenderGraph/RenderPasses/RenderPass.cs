﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
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
	private readonly List<Type> readRtHandles = new();
	protected readonly List<string> keywords = new();

	public RenderPass()
	{
		PropertyBlock = new();
	}

	public MaterialPropertyBlock PropertyBlock { get; private set; }
	public CommandBuffer Command { get; private set; }
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
		readRtHandles.Clear();
		keywords.Clear();
		PropertyBlock.Clear();
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

	public void AddKeyword(string keyword)
	{
		keywords.Add(keyword);
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
			var hasResource = RenderGraph.ResourceMap.TrySetProperties(renderPassDataHandle.Item1, RenderGraph.FrameIndex, this, command);
			Assert.IsTrue(hasResource || renderPassDataHandle.Item2);
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

		if (UseProfiler)
			Command.EndSample(Name);

		Reset();
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
		var hasResource = RenderGraph.ResourceMap.TrySetInputs(handle, RenderGraph.FrameIndex, this);
		Assert.IsTrue(isOptional || hasResource);

		RenderPassDataHandles.Add((handle, isOptional));
	}
}