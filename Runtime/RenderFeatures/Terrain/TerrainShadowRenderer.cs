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
			plane.Translate(camera.transform.position);
			cullingPlanes.SetCullingPlane(i, plane);
		}

		var passData = Cull(camera.transform.position, cullingPlanes);

		var passIndex = settings.Material.FindPass("ShadowCaster");
		Assert.IsFalse(passIndex == -1, "Terrain Material has no ShadowCaster Pass");

		var size = terrainData.size;
		var position = terrain.GetPosition() - camera.transform.position;

		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Terrain Render", (VerticesPerTileEdge, size, settings, terrainData.heightmapResolution, cullingPlanes, position)))
		{
			pass.Initialize(settings.Material, terrainSystemData.indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex, shadowRequestData.Bias, shadowRequestData.SlopeBias, shadowRequestData.ZClip);

			pass.WriteDepth(shadowRequestData.Shadow);
			pass.DepthSlice = shadowRequestData.CascadeIndex;

			pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);
			pass.AddRenderPassData<TerrainRenderData>();
			pass.AddRenderPassData<AtmospherePropertiesAndTables>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<ShadowRequestData>();

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
	}
}
