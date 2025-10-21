using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

/// <summary> Has no specific functionality but can be used as a general wrapper around render functionality </summary>
public class GenericRenderPass : RenderPass<GenericRenderPass>
{
	public void WriteTexture(ResourceHandle<RenderTexture> rtHandle)
	{
		RenderGraph.RtHandleSystem.WriteResource(rtHandle, Index);
	}

	public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		// Should also clean up on post render.. but 
		Command.SetGlobalTexture(propertyName, texture);
	}

	public override void SetBuffer(string propertyName, ResourceHandle<GraphicsBuffer> buffer)
	{
		Command.SetGlobalBuffer(propertyName, GetBuffer(buffer));
	}

	public override void SetVector(int propertyName, Vector4 value)
	{
		Command.SetGlobalVector(propertyName, value);
	}
	public override void SetVectorArray(string propertyName, Vector4[] value)
	{
		Command.SetGlobalVectorArray(propertyName, value);
	}

	public override void SetFloat(string propertyName, float value)
	{
		Command.SetGlobalFloat(propertyName, value);
	}

	public override void SetFloatArray(string propertyName, float[] value)
	{
		Command.SetGlobalFloatArray(propertyName, value);
	}

	public override void SetInt(string propertyName, int value)
	{
		Command.SetGlobalInt(propertyName, value);
	}

	protected override void ExecuteRenderPassBuilder()
	{
		Assert.IsFalse(hasDefault && hasData);

		if (hasDefault)
			renderGraphBuilderDefault.Execute(Command, this);

		if (hasData)
			renderGraphBuilder.Execute(Command, this);
	}

	protected override void Execute()
	{
		// Does nothing (Eventually could do a command.setglobalbuffer or something?)
	}

	public override void SetMatrix(string propertyName, Matrix4x4 value)
	{
		Command.SetGlobalMatrix(propertyName, value);
	}

	public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value, int size, int offset)
	{
		var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
		if (size == 0)
			size = descriptor.Count * descriptor.Stride;
		Command.SetGlobalConstantBuffer(GetBuffer(value), propertyName, offset, size);
	}

	public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
	{
		Command.SetGlobalMatrixArray(propertyName, value);
	}

	protected override void SetupTargets()
	{
	}
}