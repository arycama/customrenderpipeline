using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class TerrainRenderer : TerrainRendererBase
{
	public TerrainRenderer(RenderGraph renderGraph, TerrainSettings settings, QuadtreeCull quadtreeCull) : base(renderGraph, settings, quadtreeCull)
	{
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		// Ensure terrain system data is set
		if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
			return;

		// Also ensure this is valid for current frame
		if (!renderGraph.TryGetResource<TerrainRenderData>(out var terrainRenderData))
			return;

		var terrain = terrainSystemData.terrain;
		var terrainData = terrain.terrainData;
		if (terrain == null || settings.Material == null)
			return;

		// Used by tessellation to calculate lod
		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().cullingPlanes;
		var passData = Cull(viewRenderData.transform.position, cullingPlanes, viewRenderData.viewSize);
		var passIndex = settings.Material.FindPass("Terrain");
		Assert.IsFalse(passIndex == -1, "Terrain Material has no Terrain Pass");

		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Terrain Render", cullingPlanes))
		{
			pass.Initialize(settings.Material, terrainSystemData.indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.ReadBuffer("PatchData", passData.PatchDataBuffer);

			pass.ReadResource<AtmospherePropertiesAndTables>();
			pass.ReadResource<TerrainQuadtreeData>();
			pass.ReadResource<TerrainRenderData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<VirtualTextureData>();

			pass.SetRenderFunction(static (command, pass, cullingPlanes) =>
			{
				// TODO: Put into a struct?
				pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
				var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
				for (var i = 0; i < cullingPlanes.Count; i++)
					cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

				pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
				ArrayPool<Vector4>.Release(cullingPlanesArray);
			});
		}

		using (var pass = renderGraph.AddObjectRenderPass("Render Terrain Replacement"))
		{
			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
			pass.Initialize("Terrain", viewRenderData.context, cullingResults, viewRenderData.camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque);
			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>(), SubPassFlags.None);
			pass.ReadResource<ViewData>();
		}

		//var terrainDepth = renderGraph.GetTexture(viewRenderData.viewSize, GraphicsFormat.D32_SFloat_S8_UInt, isScreenTexture: true);
		//renderGraph.SetRTHandle<TerrainDepth>(terrainDepth);

		//var cameraDepth = renderGraph.GetRTHandle<CameraDepth>();
		//using (var pass = renderGraph.AddGenericRenderPass("Terrain Depth Copy", (cameraDepth, terrainDepth)))
		//{
		//	pass.WriteTexture(terrainDepth);
		//	pass.ReadRtHandle<CameraDepth>();

		//	pass.SetRenderFunction((commmand, pass, data) =>
		//	{
		//		commmand.CopyTexture(pass.GetRenderTexture(cameraDepth), pass.GetRenderTexture(terrainDepth));
		//	});
		//}
	}
}