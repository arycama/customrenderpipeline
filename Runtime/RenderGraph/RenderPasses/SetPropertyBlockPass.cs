using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class SetPropertyBlockPass : RenderPass<SetPropertyBlockPass>
{
	public readonly MaterialPropertyBlock propertyBlock;

	public SetPropertyBlockPass()
	{
		propertyBlock = new MaterialPropertyBlock();
	}

	public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		switch (subElement)
		{
			case RenderTextureSubElement.Depth:
				propertyBlock.SetTexture(propertyName, (RenderTexture)texture, RenderTextureSubElement.Depth);
				break;
			case RenderTextureSubElement.Stencil:
				propertyBlock.SetTexture(propertyName, (RenderTexture)texture, RenderTextureSubElement.Stencil);
				break;
			default:
				propertyBlock.SetTexture(propertyName, texture);
				break;
		}
	}

	public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
	{
		propertyBlock.SetBuffer(propertyName, GetBuffer(buffer));
	}

	public override void SetVector(int propertyName, Vector4 value)
	{
		propertyBlock.SetVector(propertyName, value);
	}

	public override void SetVectorArray(string propertyName, Vector4[] value)
	{
		propertyBlock.SetVectorArray(propertyName, value);
	}

	public override void SetFloat(string propertyName, float value)
	{
		propertyBlock.SetFloat(propertyName, value);
	}

	public override void SetFloatArray(string propertyName, float[] value)
	{
		propertyBlock.SetFloatArray(propertyName, value);
	}

	public override void SetInt(string propertyName, int value)
	{
		propertyBlock.SetInt(propertyName, value);
	}

	protected override void Execute()
	{
	}

	public override void SetMatrix(string propertyName, Matrix4x4 value)
	{
		propertyBlock.SetMatrix(propertyName, value);
	}

	public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value, int size, int offset)
	{
		var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
		if (size == 0)
			size = descriptor.Count * descriptor.Stride;
		propertyBlock.SetConstantBuffer(propertyName, GetBuffer(value), offset, size);
	}

	public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
	{
		propertyBlock.SetMatrixArray(propertyName, value);
	}

	protected override void SetupTargets()
	{
	}

	protected override void ExecuteRenderPassBuilder()
	{
		Assert.IsFalse(hasDefault && hasData);

		if (hasDefault)
			renderGraphBuilderDefault.Execute(Command, this);

		if (hasData)
			renderGraphBuilder.Execute(Command, this);
	}
}