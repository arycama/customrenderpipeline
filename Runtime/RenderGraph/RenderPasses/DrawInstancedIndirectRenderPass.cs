using UnityEngine;
using UnityEngine.Rendering;

public class DrawInstancedIndirectRenderPass<T> : DrawRenderPass<T>
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

	public void Initialize(Mesh mesh, int submeshIndex, Material material, ResourceHandle<GraphicsBuffer> indirectArgsBuffer, int passIndex = 0, float depthBias = 0.0f, float slopeDepthBias = 0.0f, bool zClip = true, int argsOffset = 0)
	{
		this.mesh = mesh;
		this.submeshIndex = submeshIndex;
		this.material = material;
		this.passIndex = passIndex;
		this.indirectArgsBuffer = indirectArgsBuffer;
		this.depthBias = depthBias;
		this.slopeDepthBias = slopeDepthBias;
		this.zClip = zClip;
		this.argsOffset = argsOffset;

		ReadBuffer("", indirectArgsBuffer);
	}

	public override void Reset()
	{
		base.Reset();
		material = null;
		passIndex = 0;
		zClip = true;
	}

	protected override void Execute()
	{
		foreach (var keyword in keywords)
			Command.EnableKeyword(material, new LocalKeyword(material.shader, keyword));

		if (depthBias != 0.0f || slopeDepthBias != 0.0f)
			Command.SetGlobalDepthBias(depthBias, slopeDepthBias);

		Command.SetGlobalFloat("_ZClip", zClip ? 1.0f : 0.0f);
		Command.DrawMeshInstancedIndirect(mesh, submeshIndex, material, passIndex, RenderGraph.BufferHandleSystem.GetResource(indirectArgsBuffer), argsOffset, PropertyBlock);
		Command.SetGlobalFloat("_ZClip", 1.0f);

		if (depthBias != 0.0f || slopeDepthBias != 0.0f)
			Command.SetGlobalDepthBias(0.0f, 0.0f);

		foreach (var keyword in keywords)
			Command.DisableKeyword(material, new LocalKeyword(material.shader, keyword));
	}
}