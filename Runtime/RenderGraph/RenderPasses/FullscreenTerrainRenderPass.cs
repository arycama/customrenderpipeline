using UnityEngine;

public class FullscreenTerrainRenderPass<T> : FullscreenRenderPass<T>
{
	private Terrain terrain;

	public void Initialize(Material material, Terrain terrain, int passIndex = 0, int primitiveCount = 1)
	{
		this.terrain = terrain;
		base.Initialize(material, passIndex, primitiveCount);
	}

	public override void PreExecute()
	{
		terrain.GetSplatMaterialPropertyBlock(PropertyBlock);
	}
}
