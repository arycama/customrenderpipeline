using UnityEngine;

public class FullscreenRenderPass : DrawRenderPass
{
	private Material material;
	private int passIndex;
	private int primitiveCount;

	public override string ToString()
	{
		return $"{Name} {material} {passIndex}";
	}

	public virtual void Initialize(Material material, int passIndex = 0, int primitiveCount = 1, string keyword = null)
	{
		this.material = material;
		this.passIndex = passIndex;
		this.primitiveCount = primitiveCount;
		Keyword = keyword;
	}

	protected override void Execute()
	{
		if (!string.IsNullOrEmpty(Keyword))
		{
			//keyword = new LocalKeyword(material.shader, Keyword);
			Command.EnableShaderKeyword(Keyword);
		}

		Command.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3 * primitiveCount, 1, propertyBlock);

		if (!string.IsNullOrEmpty(Keyword))
		{
			Command.DisableShaderKeyword(Keyword);
			Keyword = null;
		}

		material = null;
		passIndex = 0;
		primitiveCount = 1;
		propertyBlock.Clear();
	}
}
