using UnityEngine;
using UnityEngine.Assertions;

public class TerrainShadowRenderer : TerrainRendererBase
{
	public TerrainShadowRenderer(RenderGraph renderGraph, TerrainSettings settings, QuadtreeCull quadtreeCull) : base(renderGraph, settings, quadtreeCull)
	{
	}

	public override void Render(ViewRenderData viewRenderData)
    {
		if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
			return;

		// Also ensure this is valid for current frame
		if (!renderGraph.TryGetResource<TerrainRenderData>(out var terrainRenderData))
			return;

		var terrain = terrainSystemData.terrain;
		var terrainData = terrainSystemData.terrainData;

		if (terrainSystemData.terrain == null || settings.Material == null)
			return;

		var shadowRequestData = renderGraph.GetResource<ShadowRequestData>();
		var shadowRequest = shadowRequestData.ShadowRequest;

		var cullingPlanes = new CullingPlanes() { Count = shadowRequest.ShadowSplitData.cullingPlaneCount };
		for (var i = 0; i < cullingPlanes.Count; i++)
		{
			var plane = shadowRequest.ShadowSplitData.GetCullingPlane(i);
			plane.Translate(viewRenderData.transform.position);
			cullingPlanes.SetCullingPlane(i, plane);
		}

		var passData = Cull(viewRenderData.transform.position, cullingPlanes, viewRenderData.viewSize);

		var passIndex = settings.Material.FindPass("ShadowCaster");
		Assert.IsFalse(passIndex == -1, "Terrain Material has no ShadowCaster Pass");

		var size = terrainData.size;
		var position = terrain.GetPosition() - viewRenderData.transform.position;

		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Terrain Render", cullingPlanes))
		{
			pass.Initialize(settings.Material, terrainSystemData.indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex, shadowRequestData.Bias, shadowRequestData.SlopeBias, shadowRequestData.ZClip);

			pass.WriteDepth(shadowRequestData.Shadow);
			pass.DepthSlice = shadowRequestData.CascadeIndex;

			pass.ReadBuffer("PatchData", passData.PatchDataBuffer);

			pass.ReadResource<ShadowRequestData>();
			pass.ReadResource<TerrainRenderData>();
			pass.ReadResource<TerrainQuadtreeData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<VirtualTextureData>();

			pass.SetRenderFunction(static (command, pass, cullingPlanes) =>
			{
				pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);

				var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
				for (var i = 0; i < cullingPlanes.Count; i++)
					cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

				pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
				ArrayPool<Vector4>.Release(cullingPlanesArray);
			});
		}
	}
}
