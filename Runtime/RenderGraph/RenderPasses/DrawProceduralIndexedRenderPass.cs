using UnityEngine;

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

	protected override void Execute()
	{
		var indices = GetBuffer(indexBuffer);
		Command.DrawProcedural(indices, matrix, material, passIndex, topology, indices.count, 1, propertyBlock);
		material = null;
		passIndex = 0;
		matrix = default;
		propertyBlock.Clear();
	}
}
