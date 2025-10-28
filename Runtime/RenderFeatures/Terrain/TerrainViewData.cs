using UnityEngine;
using UnityEngine.Rendering;
using static Math;

public class TerrainViewData : CameraRenderFeature
{
	private readonly TerrainSystem terrainSystem;
	private readonly TerrainSettings settings;

	public TerrainViewData(RenderGraph renderGraph, TerrainSystem terrainSystem, TerrainSettings settings) : base(renderGraph)
	{
		this.terrainSystem = terrainSystem;
		this.settings = settings;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var terrain = terrainSystem.Terrain;
		if (terrain == null)
			return;

		var terrainData = terrain.terrainData;

		var position = terrainSystem.Terrain.GetPosition() - camera.transform.position;
		var size = (Float3)terrainData.size;
		var terrainScaleOffset = new Vector4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z);
		var terrainRemapHalfTexel = GraphicsUtilities.HalfTexelRemap(position.XZ(), size.xz, Vector2.one * terrainSystem.TerrainData.heightmapResolution);
		var terrainHeightOffset = position.y;
		var heightmapResolution = terrainData.heightmapResolution;

		renderGraph.SetResource(new TerrainRenderData(terrainSystem.diffuseArray, terrainSystem.normalMapArray, terrainSystem.maskMapArray, terrainSystem.heightmap, terrainSystem.normalmap, terrainSystem.idMap, terrainSystem.TerrainData.holesTexture, terrainRemapHalfTexel, terrainScaleOffset, size, size.y, terrainHeightOffset, terrainSystem.TerrainData.alphamapResolution, terrainSystem.terrainLayerData, terrainSystem.aoMap, terrainSystem.Terrain.GetPosition()));

		renderGraph.SetResource(new TerrainQuadtreeData(renderGraph.SetConstantBuffer
		((
			new Float4(size.xz, (terrain.GetPosition() - camera.transform.position).XZ()),
			GraphicsUtilities.HalfTexelRemap(heightmapResolution),
			Rcp(settings.CellCount * settings.PatchVertices),
			Rcp(settings.PatchVertices),
			Rcp(settings.CellCount),
			settings.PatchVertices + 1,
			settings.PatchVertices
		))));
	}
}