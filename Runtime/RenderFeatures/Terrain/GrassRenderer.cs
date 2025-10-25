using System;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

public class GrassRenderer : CameraRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField] public bool Update { get; private set; } = true;
		[field: SerializeField] public bool CastShadow { get; private set; } = false;
		[field: SerializeField] public int PatchSize { get; private set; } = 32;
		[field: SerializeField] public Material Material { get; private set; }
	}

	private readonly Settings settings;
	private readonly TerrainSystem terrainSystem;
	private readonly QuadtreeCull quadtreeCull;
	private ResourceHandle<GraphicsBuffer> indexBuffer;
	private int previousVertexCount;

	public GrassRenderer(Settings settings, TerrainSystem terrainSystem, RenderGraph renderGraph, QuadtreeCull quadtreeCull) : base(renderGraph)
	{
		this.settings = settings;
		this.terrainSystem = terrainSystem;
		this.quadtreeCull = quadtreeCull;
	}

	protected override void Cleanup(bool disposing)
	{
		if (previousVertexCount != 0)
			renderGraph.ReleasePersistentResource(indexBuffer);
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		var material = settings.Material;
		if (material == null)
			return;

		var terrainSystemData = renderGraph.GetResource<TerrainSystemData>();
		var terrain = terrainSystemData.terrain;
		if (terrain == null)
			return;

		var bladeDensity = (int)material.GetFloat("_BladeDensity");
		var bladeCount = settings.PatchSize * bladeDensity;
		var vertexCount = bladeCount * bladeCount;
		if(vertexCount != previousVertexCount)
		{
			if (previousVertexCount != 0)
				renderGraph.ReleasePersistentResource(indexBuffer);

			indexBuffer = renderGraph.GetQuadIndexBuffer(vertexCount, false);
			previousVertexCount = vertexCount;
		}

		// Need to resize buffer for visible indices
		var patchCounts = Vector2Int.FloorToInt(terrain.terrainData.size.XZ() / settings.PatchSize);
		var terrainResolution = terrain.terrainData.heightmapResolution;

		// Culling planes
		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().cullingPlanes;
		var height = material.GetFloat("_Height");

		var viewPosition = camera.transform.position;

		var terrainData = terrainSystemData.terrainData;
		var position = terrain.GetPosition() - viewPosition;
		var positionOffset = new Vector4(terrainData.size.x, terrainData.size.z, position.x, position.z);
		var mipCount = Texture2DExtensions.MipCount(terrainData.heightmapResolution) - 1;

		var edgeLength = material.GetFloat("_EdgeLength");
		var maxHeightOffset = height;
		var cellCount = patchCounts.x;

		var quadtreeCullResults = quadtreeCull.Cull(cellCount, viewPosition, cullingPlanes, vertexCount * 6, edgeLength, 1, positionOffset, true, camera.ViewSize(), true, terrainSystemData.minMaxHeights, terrainData.size.y, position.y, mipCount, maxHeightOffset);

		var size = terrainSystemData.terrainData.size;
		var heightmapResolution = terrainSystemData.terrainData.heightmapResolution;



		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Render Grass", 
		(
			bladeCount, 
			quadtreeCullResults, 
			terrain,
			size,
			cellCount,
			position,
			cullingPlanes,
			heightmapResolution
		)))
		{
			pass.Initialize(material, indexBuffer, quadtreeCullResults.IndirectArgsBuffer);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraVelocity>());

			pass.ReadBuffer("_PatchData", quadtreeCullResults.PatchDataBuffer);

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<TerrainRenderData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("BladeCount", data.bladeCount);
				pass.SetVector("_TerrainSize", (Float3)data.terrain.terrainData.size);
				pass.SetVector("_TerrainPosition", (Float3)data.terrain.GetPosition());

				var scaleOffset = new Vector4(data.size.x / data.cellCount, data.size.z / data.cellCount, data.position.x, data.position.z);
				pass.SetVector("_PatchScaleOffset", scaleOffset);
				//pass.SetVector("_SpacingScale", new Vector4(data.size.x / data.cellCount / data.PatchVertices, data.size.z / data.cellCount / data.PatchVertices, data.position.x, data.position.z));
				pass.SetFloat("_PatchUvScale", 1f / data.cellCount);

				pass.SetFloat("_HeightUvScale", 1f / data.cellCount * (1.0f - 1f / data.heightmapResolution));
				pass.SetFloat("_HeightUvOffset", 0.5f / data.heightmapResolution);

				pass.SetFloat("_MaxLod", Mathf.Log(data.cellCount, 2));

				pass.SetInt("_CullingPlanesCount", data.cullingPlanes.Count);
				var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
				for (var i = 0; i < data.cullingPlanes.Count; i++)
					cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

				pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
			});
		}
	}
}