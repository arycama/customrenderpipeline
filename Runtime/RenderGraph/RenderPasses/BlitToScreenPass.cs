using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class BlitToScreenPass : RenderPass
{
	private readonly MaterialPropertyBlock propertyBlock;
	private Material material;
	private int passIndex;

	public override string ToString()
	{
		return $"{Name} {material} {passIndex}";
	}

	public BlitToScreenPass()
	{
		propertyBlock = new();
	}

	public override void Reset()
	{
		base.Reset();
		material = null;
	}

	public void Initialize(Material material, int passIndex = 0)
	{
		this.material = material;
		this.passIndex = passIndex;
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


	public override void SetVector(int propertyName, Float4 value)
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
		Command.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1, propertyBlock);

		material = null;
		passIndex = 0;
	}

	public override void SetMatrix(string propertyName, Matrix4x4 value)
	{
		propertyBlock.SetMatrix(propertyName, value);
	}

	public override void SetConstantBuffer(string propertyName, ResourceHandle<GraphicsBuffer> value, int size, int offset)
	{
		var descriptor = RenderGraph.BufferHandleSystem.GetDescriptor(value);
		if(size == 0)
			size = descriptor.Count * descriptor.Stride;
		propertyBlock.SetConstantBuffer(propertyName, GetBuffer(value), offset, size);
	}

	public override void SetMatrixArray(string propertyName, Matrix4x4[] value)
	{
		propertyBlock.SetMatrixArray(propertyName, value);
	}

	protected override void SetupTargets()
	{
		Command.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
	}
}