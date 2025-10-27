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
	private readonly QuadtreeCull quadtreeCull;
	private ResourceHandle<GraphicsBuffer> indexBuffer, instanceDataBuffer;
	private int previousVertexCount;
	private readonly ComputeShader grassDataComputeShader;

	public GrassRenderer(Settings settings, RenderGraph renderGraph, QuadtreeCull quadtreeCull) : base(renderGraph)
	{
		this.settings = settings;
		this.quadtreeCull = quadtreeCull;
		this.grassDataComputeShader = Resources.Load<ComputeShader>("GpuInstancedRendering/GrassData");
	}

	protected override void Cleanup(bool disposing)
	{
		if (previousVertexCount != 0)
		{
			renderGraph.ReleasePersistentResource(indexBuffer);
			renderGraph.ReleasePersistentResource(instanceDataBuffer);
		}
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
			{
				renderGraph.ReleasePersistentResource(indexBuffer);
				renderGraph.ReleasePersistentResource(instanceDataBuffer);
			}

			indexBuffer = renderGraph.GetQuadIndexBuffer(vertexCount, false);
			instanceDataBuffer = renderGraph.GetBuffer(vertexCount, isPersistent: true);
			previousVertexCount = vertexCount;

			// Just initialize this once, should do it whenever data changes but
			using (var pass = renderGraph.AddComputeRenderPass("Grass Data Init", (bladeCount, material.GetFloat("_MinScale"), material.GetFloat("_Bend"))))
			{
				pass.Initialize(grassDataComputeShader, 0, vertexCount);

				pass.WriteBuffer("InstanceData", instanceDataBuffer);

				pass.SetRenderFunction((command, pass, data) =>
				{
					pass.SetFloat("BladeCount", data.bladeCount);
					pass.SetFloat("MinScale", data.Item2);
					pass.SetFloat("Bend", data.Item3);
				});
			}
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
		var cellCount = patchCounts.x;

		var quadtreeCullResults = quadtreeCull.Cull(cellCount, cullingPlanes, vertexCount * 6, edgeLength, positionOffset, true, camera.ViewSize(), true, terrainSystemData.minMaxHeights, terrainData.size.y, position.y, mipCount, height);

		var size = terrainSystemData.terrainData.size;
		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Render Grass",
		(
			bladeCount,
			patchScaleOffset: new Float4(size.x / cellCount, size.z / cellCount, position.x, position.z)
		)))
		{
			pass.Initialize(material, indexBuffer, quadtreeCullResults.IndirectArgsBuffer);

			pass.WriteDepth(renderGraph.GetRTHandle<CameraDepth>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferAlbedoMetallic>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferNormalRoughness>());
			pass.WriteTexture(renderGraph.GetRTHandle<GBufferBentNormalOcclusion>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraTarget>());
			pass.WriteTexture(renderGraph.GetRTHandle<CameraVelocity>());

			pass.ReadBuffer("PatchData", quadtreeCullResults.PatchDataBuffer);
			pass.ReadBuffer("InstanceData", instanceDataBuffer);

			pass.AddRenderPassData<FrameData>();
			pass.AddRenderPassData<ViewData>();
			pass.AddRenderPassData<TemporalAAData>();
			pass.AddRenderPassData<TerrainRenderData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("BladeCount", data.bladeCount);
				pass.SetVector("PatchScaleOffset", data.patchScaleOffset);
			});
		}
	}
}