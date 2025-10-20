using UnityEngine;

public class DrawProceduralRenderPass : DrawRenderPass
{
	private Material material;
	private int passIndex;
	private int primitiveCount;
	private int vertexCount;
	private Matrix4x4 matrix;
	private MeshTopology topology;

	public override string ToString()
	{
		return $"{Name} {material} {passIndex}";
	}

	public void Initialize(Material material, Matrix4x4 matrix, int passIndex = 0, int vertexCount = 3, int primitiveCount = 1, MeshTopology topology = MeshTopology.Triangles)
	{
		this.material = material;
		this.passIndex = passIndex;
		this.vertexCount = vertexCount;
		this.primitiveCount = primitiveCount;
		this.matrix = matrix;
		this.topology = topology;
	}

	protected override void Execute()
	{
		Command.DrawProcedural(matrix, material, passIndex, topology, vertexCount * primitiveCount, 1, propertyBlock);
		material = null;
		passIndex = 0;
		primitiveCount = 1;
		matrix = default;
		propertyBlock.Clear();
	}
}
