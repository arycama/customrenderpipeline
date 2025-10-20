using UnityEngine;

public class DrawInstancedIndirectRenderPass : DrawRenderPass
{
	private Material material;
	private int passIndex;
	private ResourceHandle<GraphicsBuffer> indirectArgsBuffer;
	private float depthBias, slopeDepthBias;
	private bool zClip;
	private int submeshIndex, argsOffset;
	private Mesh mesh;

	public override string ToString()
	{
		return $"{Name} {material} {passIndex}";
	}

	public void Initialize(Mesh mesh, int submeshIndex, Material material, ResourceHandle<GraphicsBuffer> indirectArgsBuffer, int passIndex = 0, string keyword = null, float depthBias = 0.0f, float slopeDepthBias = 0.0f, bool zClip = true, int argsOffset = 0)
	{
		this.mesh = mesh;
		this.submeshIndex = submeshIndex;
		this.material = material;
		this.passIndex = passIndex;
		Keyword = keyword;
		this.indirectArgsBuffer = indirectArgsBuffer;
		this.depthBias = depthBias;
		this.slopeDepthBias = slopeDepthBias;
		this.zClip = zClip;
		this.argsOffset = argsOffset;

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
		Command.DrawMeshInstancedIndirect(mesh, submeshIndex, material, passIndex, RenderGraph.BufferHandleSystem.GetResource(indirectArgsBuffer), argsOffset, propertyBlock);
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