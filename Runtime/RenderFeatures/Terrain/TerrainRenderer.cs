using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class TerrainRenderer : TerrainRendererBase
{
	public TerrainRenderer(TerrainSettings settings, RenderGraph renderGraph) : base(renderGraph, settings)
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

		using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectIndexedRenderPass>("Terrain Render"))
		{
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.None, RenderBufferLoadAction.DontCare);

			pass.Initialize(settings.Material, terrainSystemData.indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);
			pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<TerrainRenderData>();
			pass.AddRenderPassData<ViewData>();

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("_VerticesPerEdge", VerticesPerTileEdge);
				pass.SetInt("_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
				pass.SetFloat("_RcpVerticesPerEdge", 1f / VerticesPerTileEdge);
				pass.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

				var scaleOffset = new Vector4(size.x / settings.CellCount, size.z / settings.CellCount, position.x, position.z);
				pass.SetVector("_PatchScaleOffset", scaleOffset);
				pass.SetVector("_SpacingScale", new Vector4(size.x / settings.CellCount / settings.PatchVertices, size.z / settings.CellCount / settings.PatchVertices, position.x, position.z));
				pass.SetFloat("_PatchUvScale", 1f / settings.CellCount);

				pass.SetFloat("_HeightUvScale", 1f / settings.CellCount * (1.0f - 1f / terrainSystemData.terrainData.heightmapResolution));
				pass.SetFloat("_HeightUvOffset", 0.5f / terrainSystemData.terrainData.heightmapResolution);

				pass.SetFloat("_MaxLod", Mathf.Log(settings.CellCount, 2));

				pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
				var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
				for (var i = 0; i < cullingPlanes.Count; i++)
					cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

				pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
				ArrayPool<Vector4>.Release(cullingPlanesArray);
			});
		}

		var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;

		using (var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Render Terrain Replacement"))
		{
			pass.Initialize("Terrain", context, cullingResults, camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), RenderTargetFlags.None);
			pass.AddRenderPassData<ViewData>();
		}
	}
}