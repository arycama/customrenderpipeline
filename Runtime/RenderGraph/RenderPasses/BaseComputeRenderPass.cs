using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public abstract class BaseComputeRenderPass<T> : RenderPass<T>
{
	protected ComputeShader computeShader;
	protected int kernelIndex;
	protected readonly List<(ResourceHandle<RenderTexture>, int, int)> colorBindings = new();
	protected readonly List<string> keywords = new();

	public override void Reset()
	{
		base.Reset();
		colorBindings.Clear();
		keywords.Clear();
	}

	public void WriteTexture(int propertyId, ResourceHandle<RenderTexture> rtHandle, int mip = 0)
	{
		RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);
		colorBindings.Add(new(rtHandle, propertyId, mip));

		var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(rtHandle);
		descriptor = new RtHandleDescriptor(descriptor.width, descriptor.height, descriptor.format, descriptor.volumeDepth, descriptor.dimension, descriptor.isScreenTexture, descriptor.hasMips, descriptor.autoGenerateMips, true, descriptor.isExactSize, descriptor.clearFlags, descriptor.clearColor, descriptor.clearDepth, descriptor.clearStencil);
		RenderGraph.RtHandleSystem.SetDescriptor(rtHandle, descriptor);
	}

	public void WriteTexture(string propertyName, ResourceHandle<RenderTexture> texture, int mip = 0)
	{
		WriteTexture(Shader.PropertyToID(propertyName), texture, mip);
	}

	public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		Command.SetComputeTextureParam(computeShader, kernelIndex, propertyName, texture, mip, subElement);
	}

	public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
	{
		Command.SetComputeBufferParam(computeShader, kernelIndex, propertyName, GetBuffer(buffer));
	}

	public override void SetVector(int propertyName, Float4 value)
	{
		Command.SetComputeVectorParam(computeShader, propertyName, value);
	}

	public override void SetVectorArray(string propertyName, Vector4[] value)
	{
		Command.SetComputeVectorArrayParam(computeShader, propertyName, value);
	}

	public override void SetFloat(string propertyName, float value)
	{
		Command.SetComputeFloatParam(computeShader, propertyName, value);
	}

	public override void SetFloatArray(string propertyName, float[] value)
	{
		Command.SetComputeFloatParams(computeShader, propertyName, value);
	}

	public override void SetInt(string propertyName, int value)
	{
		Command.SetComputeIntParam(computeShader, propertyName, value);
	}

	protected override void SetupTargets()
	{
		for (var i = 0; i < colorBindings.Count; i++)
			Command.SetComputeTextureParam(computeShader, kernelIndex, colorBindings[i].Item2, GetRenderTexture(colorBindings[i].Item1), colorBindings[i].Item3);
	}

	public override void SetMatrix(string propertyName, Matrix4x4 value)
	{
		Command.SetComputeMatrixParam(computeShader, propertyName, value);
	}

	public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value, int size, int offset)
	{
		var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);

		if(size == 0)
			size = descriptor.Count * descriptor.Stride;

		Command.SetComputeConstantBufferParam(computeShader, propertyName, GetBuffer(value), offset, size);
	}

	public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
	{
		Command.SetComputeMatrixArrayParam(computeShader, propertyName, value);
	}

	public void AddKeyword(string keyword)
	{
		keywords.Add(keyword);
	}

	protected sealed override void PostExecute()
	{
		foreach (var colorTarget in colorBindings)
		{
			var descriptor = RenderGraph.RtHandleSystem.GetDescriptor(colorTarget.Item1);
			if (descriptor.autoGenerateMips && descriptor.hasMips)
				Command.GenerateMips(GetRenderTexture(colorTarget.Item1));
		}

		colorBindings.Clear();
	}
}
