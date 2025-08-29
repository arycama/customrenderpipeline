using UnityEngine;
using UnityEngine.Rendering;

public class DrawProceduralIndirectIndexedRenderPass : GraphicsRenderPass
{
	public readonly MaterialPropertyBlock propertyBlock;
	private Material material;
	private int passIndex;
	private ResourceHandle<GraphicsBuffer> indexBuffer;
	private ResourceHandle<GraphicsBuffer> indirectArgsBuffer;
	private MeshTopology topology;
	private float depthBias, slopeDepthBias;
	private bool zClip;
	private int argsOffset;

	public string Keyword { get; set; }

	public DrawProceduralIndirectIndexedRenderPass()
	{
		propertyBlock = new MaterialPropertyBlock();
	}

	public override string ToString()
	{
		return $"{Name} {material} {passIndex}";
	}

	public void Initialize(Material material, ResourceHandle<GraphicsBuffer> indexBuffer, ResourceHandle<GraphicsBuffer> indirectArgsBuffer, MeshTopology topology = MeshTopology.Triangles, int passIndex = 0, string keyword = null, float depthBias = 0.0f, float slopeDepthBias = 0.0f, bool zClip = true, int argsOffset = 0)
	{
		this.material = material;
		this.passIndex = passIndex;
		Keyword = keyword;
		this.indexBuffer = indexBuffer;
		this.indirectArgsBuffer = indirectArgsBuffer;
		this.topology = topology;
		this.depthBias = depthBias;
		this.slopeDepthBias = slopeDepthBias;
		this.zClip = zClip;
		this.argsOffset = argsOffset;

		ReadBuffer("", indexBuffer);
		ReadBuffer("", indirectArgsBuffer);
	}

	public override void SetTexture(int propertyName, Texture texture, int mip = 0, RenderTextureSubElement subElement = RenderTextureSubElement.Default)
	{
		propertyBlock.SetTexture(propertyName, texture);
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
		if (!string.IsNullOrEmpty(Keyword))
		{
			Command.EnableShaderKeyword(Keyword);
		}

		if (depthBias != 0.0f || slopeDepthBias != 0.0f)
			Command.SetGlobalDepthBias(depthBias, slopeDepthBias);

		Command.SetGlobalFloat("_ZClip", zClip ? 1.0f : 0.0f);
		Command.DrawProceduralIndirect(GetBuffer(indexBuffer), Matrix4x4.identity, material, passIndex, topology, GetBuffer(indirectArgsBuffer), argsOffset, propertyBlock);
		Command.SetGlobalFloat("_ZClip", 1.0f);

		if (depthBias != 0.0f || slopeDepthBias != 0.0f)
			Command.SetGlobalDepthBias(0.0f, 0.0f);

		if (!string.IsNullOrEmpty(Keyword))
		{
			Command.DisableShaderKeyword(Keyword);
			Keyword = null;
		}

		material = null;
		passIndex = 0;
		propertyBlock.Clear();
		zClip = true;
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
}
