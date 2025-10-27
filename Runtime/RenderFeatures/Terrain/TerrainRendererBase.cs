using UnityEngine;

public abstract partial class TerrainRendererBase : CameraRenderFeature
{
	protected readonly TerrainSettings settings;
	protected readonly QuadtreeCull quadtreeCull;

	protected TerrainRendererBase(RenderGraph renderGraph, TerrainSettings settings, QuadtreeCull quadtreeCull) : base(renderGraph)
	{
		this.settings = settings;
		this.quadtreeCull = quadtreeCull;
	}

	protected QuadtreeCullResult Cull(Vector3 viewPosition, CullingPlanes cullingPlanes, Int2 viewSize)
	{
		var terrainSystemData = renderGraph.GetResource<TerrainSystemData>();
		var terrain = terrainSystemData.terrain;
		var terrainData = terrainSystemData.terrainData;
		var position = terrain.GetPosition() - viewPosition;
		var positionOffset = new Vector4(terrainData.size.x, terrainData.size.z, position.x, position.z);
		var mipCount = Texture2DExtensions.MipCount(terrainData.heightmapResolution) - 1;
		var indexCount = settings.PatchVertices * settings.PatchVertices * 4;

		return quadtreeCull.Cull(settings.CellCount, cullingPlanes, indexCount, settings.EdgeLength * settings.PatchVertices, positionOffset, false, viewSize, true, terrainSystemData.minMaxHeights, terrainData.size.y, position.y, mipCount);
	}
}