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

		var position = terrainSystem.Terrain.GetPosition() - camera.transform.position;
		var size = (Float3)terrain.terrainData.size;

		renderGraph.SetResource<TerrainRenderData>(new(terrainSystem.diffuseArray, terrainSystem.normalMapArray, terrainSystem.maskMapArray, terrainSystem.heightmap, terrainSystem.normalmap, terrainSystem.idMap, terrainSystem.TerrainData.holesTexture, terrainSystem.terrainLayerData, terrainSystem.aoMap,

		renderGraph.SetConstantBuffer
		((
			size,
			(float)terrainSystem.TerrainData.alphamapResolution,
			terrainSystem.Terrain.GetPosition(),
			size.y,
			GraphicsUtilities.HalfTexelRemap(position.XZ(), size.xz, Vector2.one * terrainSystem.TerrainData.heightmapResolution),
			new Float4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z),
			GraphicsUtilities.HalfTexelRemap(terrain.terrainData.heightmapResolution),
			position.y,
			0f
		))));

		renderGraph.SetResource(new TerrainQuadtreeData(renderGraph.SetConstantBuffer
		((
			new Float4(size.xz, (terrain.GetPosition() - camera.transform.position).XZ()),
			GraphicsUtilities.HalfTexelRemap(terrain.terrainData.heightmapResolution),
			Rcp(settings.CellCount * settings.PatchVertices),
			Rcp(settings.PatchVertices),
			Rcp(settings.CellCount),
			settings.PatchVertices + 1,
			settings.PatchVertices
		))));
	}
}