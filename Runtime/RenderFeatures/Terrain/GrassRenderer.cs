using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class GrassRenderer : ViewRenderFeature
{
	[Serializable]
	public class Settings
	{
		[field: SerializeField] public bool Enabled { get; private set; } = true;
		[field: SerializeField] public bool CastShadow { get; private set; } = false;
		[field: SerializeField] public int PatchSize { get; private set; } = 32;
		[field: SerializeField] public Material Material { get; private set; }
	}

	private readonly Settings settings;
	private readonly QuadtreeCull quadtreeCull;
	private ResourceHandle<GraphicsBuffer> indexBuffer, instanceDataBuffer;
	private int previousVertexCount;
	private readonly ComputeShader grassDataComputeShader;
	private bool isInitialized;
	private ResourceHandle<RenderTexture> coverageMap;
	private Material grassCoverageMaterial;
    private TerrainSystem terrainSystem;

    private int idMapVersion, heightMapVersion;
    private int previousResolution;

	public GrassRenderer(Settings settings, RenderGraph renderGraph, QuadtreeCull quadtreeCull, TerrainSystem terrainSystem) : base(renderGraph)
	{
		this.settings = settings;
		this.quadtreeCull = quadtreeCull;
        this.terrainSystem = terrainSystem;
		grassDataComputeShader = Resources.Load<ComputeShader>("GpuInstancedRendering/GrassData");
		grassCoverageMaterial = new Material(Shader.Find("Hidden/Grass Coverage")){ hideFlags = HideFlags.HideAndDontSave };
	}

	protected override void Cleanup(bool disposing)
	{
		if (previousVertexCount != 0)
		{
			renderGraph.ReleasePersistentResource(indexBuffer, -1);
			renderGraph.ReleasePersistentResource(instanceDataBuffer, -1);
		}

        if (isInitialized)
            renderGraph.ReleasePersistentResource(coverageMap, -1);
	}

	public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
        if (viewPassData.cameraType == CameraType.Preview)
            return;

        if (!settings.Enabled)
            return;

		var material = settings.Material;
		if (material == null)
			return;

		if (!renderGraph.TryGetResource<TerrainSystemData>(out var terrainSystemData))
			return;

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
				renderGraph.ReleasePersistentResource(indexBuffer, -1);
				renderGraph.ReleasePersistentResource(instanceDataBuffer, -1);
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

        // Calculate coverage map if needed
        // TODO: Only update the required region
        if (idMapVersion != terrainSystem.IdMapVersion)
		{
            idMapVersion = terrainSystem.IdMapVersion;

            var resolution = terrain.terrainData.alphamapResolution;
            RectInt region;
            if (!isInitialized || previousResolution != resolution)
            {
                if (isInitialized)
                    renderGraph.ReleasePersistentResource(coverageMap, renderGraph.RenderPassCount);

                coverageMap = renderGraph.GetTexture(resolution, GraphicsFormat.R8_UNorm, isPersistent: true);
                previousResolution = resolution;
                isInitialized = true;
                region = new RectInt(0, 0, resolution, resolution);
            }
            else
            {
                region = terrainSystem.LastIdUpdateRect;
            }

            var viewport = new Rect(new Vector2(region.position.x, resolution - region.position.y - region.size.y), region.size);

            using (var pass = renderGraph.AddFullscreenRenderPass("Grass Coverage Update", viewport))
			{
				pass.Initialize(grassCoverageMaterial, terrain.terrainData.alphamapResolution);
				pass.WriteTexture(coverageMap);
				pass.ReadResource<TerrainFrameData>();
				pass.ReadResource<TerrainViewData>();

                pass.SetRenderFunction((command, pass, viewport) =>
                {
                    command.EnableScissorRect(viewport);
                });
			}

            using (var pass = renderGraph.AddGenericRenderPass("Grass Coverage Update"))
            {
                pass.SetRenderFunction((command, pass, viewport) =>
                {
                    command.DisableScissorRect();
                });
            }
		}

		// Need to resize buffer for visible indices
		var patchCounts = Vector2Int.FloorToInt(terrain.terrainData.size.XZ() / settings.PatchSize);
		var terrainResolution = terrain.terrainData.heightmapResolution;

		// Culling planes
		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().cullingPlanes;
		var height = material.GetFloat("_Height");

        var terrainData = terrainSystemData.terrainData;
		var position = terrain.GetPosition() - viewPassData.position;
		var positionOffset = new Vector4(terrainData.size.x, terrainData.size.z, position.x, position.z);
		var mipCount = Texture2DExtensions.MipCount(terrainData.heightmapResolution) - 1;

		var edgeLength = material.GetFloat("_EdgeLength");
		var cellCount = patchCounts.x;

		var quadtreeCullResults = quadtreeCull.Cull(cellCount, cullingPlanes, vertexCount * 6, edgeLength, positionOffset, true, viewPassData.viewSize, true, terrainSystemData.minMaxHeights, terrainData.size.y, position.y, mipCount, height);

		var size = terrainSystemData.terrainData.size;
		using (var pass = renderGraph.AddDrawProceduralIndirectIndexedRenderPass("Render Grass",
		(
			bladeCount,
			patchScaleOffset: new Float4(size.x / cellCount, size.z / cellCount, position.x, position.z)
		)))
		{
			pass.Initialize(material, indexBuffer, quadtreeCullResults.IndirectArgsBuffer, viewPassData.viewSize, 1, isScreenPass: true);

            pass.WriteRtHandleDepth<CameraDepth>();
            pass.WriteRtHandle<GBufferAlbedoMetallic>();
            pass.WriteRtHandle<GBufferNormalRoughness>();
            pass.WriteRtHandle<GBufferBentNormalOcclusion>();
            pass.WriteRtHandle<CameraTarget>();
            pass.WriteRtHandle<CameraVelocity>();

			pass.ReadBuffer("PatchData", quadtreeCullResults.PatchDataBuffer);
			pass.ReadBuffer("InstanceData", instanceDataBuffer);
			pass.ReadTexture("GrassCoverage", coverageMap);

			pass.ReadResource<FrameData>();
			pass.ReadResource<ViewData>();
			pass.ReadResource<TemporalAAData>();
		pass.ReadResource<TerrainFrameData>();
			pass.ReadResource<TerrainViewData>();
			pass.ReadResource<VirtualTextureData>();

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				pass.SetFloat("BladeCount", data.bladeCount);
				pass.SetVector("PatchScaleOffset", data.patchScaleOffset);
			});
		}
	}
}