using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class TerrainRenderer : TerrainRendererBase
{
	public TerrainRenderer(RenderGraph renderGraph, TerrainSettings settings, QuadtreeCull quadtreeCull) : base(renderGraph, settings, quadtreeCull)
	{
	}

	public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
        if (viewPassData.cameraType == CameraType.Preview)
            return;

        // Ensure terrain system data is set
        if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
			return;

		// Also ensure this is valid for current frame
		if (!renderGraph.TryGetResource<TerrainViewData>(out var terrainRenderData))
			return;

		var terrain = terrainSystemData.terrain;
		var terrainData = terrain.terrainData;
		if (terrain == null || settings.Material == null)
			return;

		// Used by tessellation to calculate lod
		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().cullingPlanes;
		var passData = Cull(viewPassData.position, cullingPlanes, viewPassData.viewSize);
		var passIndex = settings.Material.FindPass("Terrain");
		Assert.IsFalse(passIndex == -1, "Terrain Material has no Terrain Pass");

        renderGraph.AddProfileBeginPass("Render Terrain");

		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Terrain Render", cullingPlanes))
		{
            pass.UseProfiler = false;
            pass.Initialize(settings.Material, terrainSystemData.indexBuffer, passData.IndirectArgsBuffer, viewPassData.viewSize, 1, MeshTopology.Quads, passIndex, isScreenPass: true);
            pass.WriteRtHandleDepth<CameraDepth>();
			pass.ReadBuffer("PatchData", passData.PatchDataBuffer);

			pass.ReadResource<TerrainQuadtreeData>();
		    pass.ReadResource<TerrainFrameData>();
			pass.ReadResource<TerrainViewData>();
			pass.ReadResource<ViewData>();
            pass.ReadResource<TerrainFrameData>();

            if (pass.TryReadResource<VirtualTextureData>())
                pass.AddKeyword("VIRTUAL_TEXTURING_ON");

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
            pass.UseProfiler = false;
			var cullingResults = renderGraph.GetResource<CullingResultsData>().cullingResults;
			pass.Initialize("Terrain", context, cullingResults, RenderQueueRange.opaque, viewPassData.viewSize, viewPassData.position, viewPassData.rotation, viewPassData.sortAxis, viewPassData.distanceMetric, SortingCriteria.CommonOpaque, isScreenPass: true);
            pass.WriteRtHandleDepth<CameraDepth>();
			pass.ReadResource<ViewData>();
		}

        renderGraph.AddProfileEndPass("Render Terrain");
    }
}