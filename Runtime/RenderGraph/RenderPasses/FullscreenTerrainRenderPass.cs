using UnityEngine;

public class FullscreenTerrainRenderPass<T> : FullscreenRenderPass<T>
{
	private Terrain terrain;

	public void Initialize(Material material, Terrain terrain, int passIndex = 0, int primitiveCount = 1, string keyword = null)
	{
		this.terrain = terrain;
		base.Initialize(material, passIndex, primitiveCount, keyword);
	}

	public override void PreExecute()
	{
		terrain.GetSplatMaterialPropertyBlock(propertyBlock);
	}
}
