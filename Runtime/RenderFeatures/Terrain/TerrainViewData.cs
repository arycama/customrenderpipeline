using UnityEngine;
using UnityEngine.Rendering;

public class TerrainViewData : CameraRenderFeature
{
	private readonly TerrainSystem terrainSystem;

	public TerrainViewData(RenderGraph renderGraph, TerrainSystem terrainSystem) : base(renderGraph)
	{
		this.terrainSystem = terrainSystem;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		if (terrainSystem.terrain == null)
			return;

		var position = terrainSystem.terrain.GetPosition() - camera.transform.position;
		var size = terrainSystem.terrainData.size;
		var terrainScaleOffset = new Vector4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z);
		var terrainRemapHalfTexel = GraphicsUtilities.HalfTexelRemap(position.XZ(), size.XZ(), Vector2.one * terrainSystem.terrainData.heightmapResolution);
		var terrainHeightOffset = position.y;
		renderGraph.SetResource(new TerrainRenderData(terrainSystem.diffuseArray, terrainSystem.normalMapArray, terrainSystem.maskMapArray, terrainSystem.heightmap, terrainSystem.normalmap, terrainSystem.idMap, terrainSystem.terrainData.holesTexture, terrainRemapHalfTexel, terrainScaleOffset, size, size.y, terrainHeightOffset, terrainSystem.terrainData.alphamapResolution, terrainSystem.terrainLayerData, terrainSystem.aoMap));

		// This sets raytracing data on the terrain's material property block
		using (var pass = renderGraph.AddSetPropertyBlockPass("Terrain Data Property Block Update", terrainSystem.terrain))
		{
			var propertyBlock = pass.PropertyBlock;
			terrainSystem.terrain.GetSplatMaterialPropertyBlock(propertyBlock);
			pass.AddRenderPassData<TerrainRenderData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				var propertyBlock = pass.PropertyBlock;
				data.SetSplatMaterialPropertyBlock(propertyBlock);
			});
		}
	}
}