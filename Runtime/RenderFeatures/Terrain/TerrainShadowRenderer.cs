using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class TerrainShadowRenderer : TerrainRendererBase
{
	public TerrainShadowRenderer(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph, settings)
	{
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
			return;

		// Also ensure this is valid for current frame
		if (!renderGraph.TryGetResource<TerrainRenderData>(out var terrainRenderData))
			return;

		var terrain = terrainSystemData.Terrain;
		var terrainData = terrainSystemData.TerrainData;

		if (terrainSystemData.Terrain == null || settings.Material == null)
			return;

		var shadowRequestData = renderGraph.GetResource<ShadowRequestData>();
		var shadowRequest = shadowRequestData.ShadowRequest;

		var cullingPlanes = new CullingPlanes() { Count = shadowRequest.ShadowSplitData.cullingPlaneCount };
		for (var i = 0; i < cullingPlanes.Count; i++)
		{
			var plane = shadowRequest.ShadowSplitData.GetCullingPlane(i);
			plane.Translate(camera.transform.position);
			cullingPlanes.SetCullingPlane(i, plane);
		}

		var passData = Cull(camera.transform.position, cullingPlanes);

		var passIndex = settings.Material.FindPass("ShadowCaster");
		Assert.IsFalse(passIndex == -1, "Terrain Material has no ShadowCaster Pass");

		var size = terrainData.size;
		var position = terrain.GetPosition() - camera.transform.position;

		using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Terrain Render"))
		{
			pass.Initialize(settings.Material, terrainSystemData.IndexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex, null, shadowRequestData.Bias, shadowRequestData.SlopeBias, shadowRequestData.ZClip);

			pass.WriteTexture(shadowRequestData.Shadow);
			pass.DepthSlice = shadowRequestData.CascadeIndex;

			pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);
			pass.AddRenderPassData<TerrainRenderData>();
			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
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

				pass.SetFloat("_HeightUvScale", 1f / settings.CellCount * (1.0f - 1f / terrainData.heightmapResolution));
				pass.SetFloat("_HeightUvOffset", 0.5f / terrainData.heightmapResolution);

				pass.SetFloat("_MaxLod", Mathf.Log(settings.CellCount, 2));
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
