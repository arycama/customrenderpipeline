using UnityEngine;

public class DrawProceduralIndirectIndexedRenderPass : DrawRenderPass
{
	private Material material;
	private int passIndex;
	private ResourceHandle<GraphicsBuffer> indexBuffer;
	private ResourceHandle<GraphicsBuffer> indirectArgsBuffer;
	private MeshTopology topology;
	private float depthBias, slopeDepthBias;
	private bool zClip;
	private int argsOffset;

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
}
