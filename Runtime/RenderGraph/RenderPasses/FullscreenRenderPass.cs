using UnityEngine;
using UnityEngine.Rendering;

public class FullscreenRenderPass<T> : DrawRenderPass<T>
{
	private Material material;
	private int passIndex;
	private int primitiveCount;

	public override string ToString()
	{
		return $"{Name} {material} {passIndex}";
	}

	public virtual void Initialize(Material material, int passIndex = 0, int primitiveCount = 1)
	{
		this.material = material;
		this.passIndex = passIndex;
		this.primitiveCount = primitiveCount;
	}

	public override void Reset()
	{
		base.Reset();
		material = null;
		passIndex = 0;
		primitiveCount = 1;
	}

	protected override void Execute()
	{
		foreach (var keyword in keywords)
			Command.EnableKeyword(material, new LocalKeyword(material.shader, keyword));

		Command.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3 * primitiveCount, 1, PropertyBlock);

		foreach (var keyword in keywords)
			Command.DisableKeyword(material, new LocalKeyword(material.shader, keyword));
	}
}
