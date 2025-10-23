using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class TerrainRenderer : TerrainRendererBase
{
	public TerrainRenderer(RenderGraph renderGraph, TerrainSettings settings, QuadtreeCull quadtreeCull) : base(renderGraph, settings, quadtreeCull)
	{
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		// Ensure terrain system data is set
		if(!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
			return;

		// Also ensure this is valid for current frame
		if (!renderGraph.TryGetResource<TerrainRenderData>(out var terrainRenderData))
			return;

		if (terrainSystemData.terrain == null || settings.Material == null)
			return;

		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().cullingPlanes;
		var passData = Cull(camera.transform.position, cullingPlanes);
		var passIndex = settings.Material.FindPass("Terrain");
		Assert.IsFalse(passIndex == -1, "Terrain Material has no Terrain Pass");

		var size = terrainSystemData.terrainData.size;
		var position = terrainSystemData.terrain.GetPosition() - camera.transform.position;

		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Terrain Render", (VerticesPerTileEdge, size, settings, position, cullingPlanes, terrainSystemData.terrainData.heightmapResolution)))
		{
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.None, RenderBufferLoadAction.DontCare);

			pass.Initialize(settings.Material, terrainSystemData.indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);
			pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<TerrainRenderData>();
			pass.AddRenderPassData<ViewData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetInt("_VerticesPerEdge", data.VerticesPerTileEdge);
				pass.SetInt("_VerticesPerEdgeMinusOne", data.VerticesPerTileEdge - 1);
				pass.SetFloat("_RcpVerticesPerEdge", 1f / data.VerticesPerTileEdge);
				pass.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (data.VerticesPerTileEdge - 1));

				var scaleOffset = new Vector4(data.size.x / data.settings.CellCount, data.size.z / data.settings.CellCount, data.position.x, data.position.z);
				pass.SetVector("_PatchScaleOffset", scaleOffset);
				pass.SetVector("_SpacingScale", new Vector4(data.size.x / data.settings.CellCount / data.settings.PatchVertices, data.size.z / data.settings.CellCount / data.settings.PatchVertices, data.position.x, data.position.z));
				pass.SetFloat("_PatchUvScale", 1f / data.settings.CellCount);

				pass.SetFloat("_HeightUvScale", 1f / data.settings.CellCount * (1.0f - 1f / data.heightmapResolution));
				pass.SetFloat("_HeightUvOffset", 0.5f / data.heightmapResolution);

				pass.SetFloat("_MaxLod", Mathf.Log(data.settings.CellCount, 2));

				pass.SetInt("_CullingPlanesCount", data.cullingPlanes.Count);
				var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
				for (var i = 0; i < data.cullingPlanes.Count; i++)
					cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

				pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
				ArrayPool<Vector4>.Release(cullingPlanesArray);
			});
		}

		var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

		using (var pass = renderGraph.AddObjectRenderPass("Render Terrain Replacement"))
		{
			pass.Initialize("Terrain", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.None);
			pass.AddRenderPassData<ViewData>();
		}
	}
}