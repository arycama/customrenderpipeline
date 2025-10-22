using UnityEngine;
using UnityEngine.Rendering;

public class DrawProceduralIndexedRenderPass<T> : DrawRenderPass<T>
{
	private Material material;
	private int passIndex;
	private Matrix4x4 matrix;
	private MeshTopology topology;
	private ResourceHandle<GraphicsBuffer> indexBuffer;

	public override string ToString()
	{
		return $"{Name} {material} {passIndex}";
	}

	public void Initialize(ResourceHandle<GraphicsBuffer> indexBuffer, Material material, Matrix4x4 matrix, int passIndex = 0, MeshTopology topology = MeshTopology.Triangles)
	{
		this.material = material;
		this.passIndex = passIndex;
		this.matrix = matrix;
		this.topology = topology;
		this.indexBuffer = indexBuffer;
	}

	public override void Reset()
	{
		base.Reset();
		material = null;
		passIndex = 0;
		matrix = default;
	}

	protected override void Execute()
	{
		foreach (var keyword in keywords)
			Command.EnableKeyword(material, new LocalKeyword(material.shader, keyword));

		var indices = GetBuffer(indexBuffer);
		Command.DrawProcedural(indices, matrix, material, passIndex, topology, indices.count, 1, PropertyBlock);

		foreach (var keyword in keywords)
			Command.DisableKeyword(material, new LocalKeyword(material.shader, keyword));
	}
}
