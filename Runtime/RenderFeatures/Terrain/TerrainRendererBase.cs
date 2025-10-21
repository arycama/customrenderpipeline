using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;

public abstract partial class TerrainRendererBase : CameraRenderFeature
{
	protected readonly TerrainSettings settings;
	protected int VerticesPerTileEdge => settings.PatchVertices + 1;
	protected int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

	protected TerrainRendererBase(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph)
	{
		this.settings = settings;
	}

	protected TerrainCullResult Cull(Vector3 viewPosition, CullingPlanes cullingPlanes)
	{
		// TODO: Preload?
		var compute = Resources.Load<ComputeShader>("Terrain/TerrainQuadtreeCull");
		var indirectArgsBuffer = renderGraph.GetBuffer(5, target: GraphicsBuffer.Target.IndirectArguments);
		var patchDataBuffer = renderGraph.GetBuffer(settings.CellCount * settings.CellCount, target: GraphicsBuffer.Target.Structured);

		// We can do 32x32 cells in a single pass, larger counts need to be broken up into several passes
		var maxPassesPerDispatch = 6;
		var totalPassCount = (int)Mathf.Log(settings.CellCount, 2f) + 1;
		var dispatchCount = Mathf.Ceil(totalPassCount / (float)maxPassesPerDispatch);

		ResourceHandle<RenderTexture> tempLodId = default;
		ResourceHandle<GraphicsBuffer> lodIndirectArgsBuffer = default;
		if (dispatchCount > 1)
		{
			// If more than one dispatch, we need to write lods out to a temp texture first. Otherwise they are done via shared memory so no texture is needed
			tempLodId = renderGraph.GetTexture(settings.CellCount, settings.CellCount, GraphicsFormat.R16_UInt);
			lodIndirectArgsBuffer = renderGraph.GetBuffer(3, target: GraphicsBuffer.Target.IndirectArguments);
		}

		var tempIds = ListPool<ResourceHandle<RenderTexture>>.Get();
		for (var i = 0; i < dispatchCount - 1; i++)
		{
			var tempResolution = 1 << ((i + 1) * (maxPassesPerDispatch - 1));
			tempIds.Add(renderGraph.GetTexture(tempResolution, tempResolution, GraphicsFormat.R16_UInt));
		}

		var terrainSystemData = renderGraph.GetResource<TerrainSystemData>();
		for (var i = 0; i < dispatchCount; i++)
		{
			var isFirstPass = i == 0; // Also indicates whether this is -not- the first pass
			var passCount = Mathf.Min(maxPassesPerDispatch, totalPassCount - i * maxPassesPerDispatch);
			var index = i;

			using (var pass = renderGraph.AddComputeRenderPass("Terrain Quadtree Cull", new TerrainQuadtreeCullData
			(
				viewPosition,
				cullingPlanes,
				indirectArgsBuffer,
				totalPassCount,
				isFirstPass,
				passCount,
				index,
				QuadListIndexCount,
				terrainSystemData.terrain,
				terrainSystemData.terrainData,
				settings
			)))
			{
				if (!isFirstPass)
					pass.ReadTexture("_TempResult", tempIds[i - 1]);

				var isFinalPass = i == dispatchCount - 1; // Also indicates whether this is -not- the final pass

				var threadCount = 1 << (i * 6 + passCount - 1);
				pass.Initialize(compute, 0, threadCount, threadCount);

				if (isFirstPass)
					pass.AddKeyword("FIRST");

				if (isFinalPass)
					pass.AddKeyword("FINAL");

				if (isFinalPass && !isFirstPass)
				{
					// Final pass writes out lods to a temp texture if more than one pass was used
					pass.WriteTexture("_LodResult", tempLodId);
				}

				if (!isFinalPass)
					pass.WriteTexture("_TempResultWrite", tempIds[i]);

				pass.WriteBuffer("_IndirectArgs", indirectArgsBuffer);
				pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
				pass.ReadTexture("_TerrainHeights", terrainSystemData.minMaxHeights);
				pass.AddRenderPassData<ViewData>();

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					// First pass sets the buffer contents
					if (data.isFirstPass)
					{
						var indirectArgs = ListPool<int>.Get();
						indirectArgs.Add(data.QuadListIndexCount); // index count per instance
						indirectArgs.Add(0); // instance count (filled in later)
						indirectArgs.Add(0); // start index location
						indirectArgs.Add(0); // base vertex location
						indirectArgs.Add(0); // start instance location
						command.SetBufferData(pass.GetBuffer(data.indirectArgsBuffer), indirectArgs);
						ListPool<int>.Release(indirectArgs);
					}

					// Do up to 6 passes per dispatch.
					pass.SetInt("_PassCount", data.passCount);
					pass.SetInt("_PassOffset", 6 * data.index);
					pass.SetInt("_TotalPassCount", data.totalPassCount);

					var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
					for (var i = 0; i < data.cullingPlanes.Count; i++)
						cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

					pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
					ArrayPool<Vector4>.Release(cullingPlanesArray);

					// Snap to quad-sized increments on largest cell
					var position = data.terrain.GetPosition() - data.viewPosition;
					var positionOffset = new Vector4(data.terrainData.size.x, data.terrainData.size.z, position.x, position.z);
					pass.SetVector("_TerrainPositionOffset", positionOffset);

					pass.SetFloat("_EdgeLength", data.settings.EdgeLength * data.settings.PatchVertices);
					pass.SetInt("_CullingPlanesCount", data.cullingPlanes.Count);

					pass.SetFloat("_InputScale", data.terrainData.size.y);
					pass.SetFloat("_InputOffset", position.y);

					pass.SetInt("_MipCount", Texture2DExtensions.MipCount(data.terrainData.heightmapResolution) - 1);
				});
			}
		}

		if (dispatchCount > 1)
		{
			using (var pass = renderGraph.AddComputeRenderPass("Terrain Quadtree Cull"))
			{
				pass.Initialize(compute, 1, normalizedDispatch: false);
				pass.WriteBuffer("_IndirectArgs", lodIndirectArgsBuffer);

				// If more than one pass needed, we need a second pass to write out lod deltas to the patch data
				// Copy count from indirect draw args so we only dispatch as many threads as needed
				pass.ReadBuffer("_IndirectArgsInput", indirectArgsBuffer);
			}

			using (var pass = renderGraph.AddIndirectComputeRenderPass("Terrain Quadtree Cull", settings))
			{
				pass.Initialize(compute, lodIndirectArgsBuffer, 2);
				pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
				pass.ReadTexture("_LodInput", tempLodId);
				pass.ReadBuffer("_IndirectArgs", indirectArgsBuffer);

				pass.SetRenderFunction(static (command, pass, settings) =>
				{
					pass.SetInt("_CellCount", settings.CellCount);
				});
			}
		}

		ListPool<ResourceHandle<RenderTexture>>.Release(tempIds);

		return new(indirectArgsBuffer, patchDataBuffer);
	}
}

internal struct TerrainQuadtreeCullData
{
	public Vector3 viewPosition;
	public CullingPlanes cullingPlanes;
	public ResourceHandle<GraphicsBuffer> indirectArgsBuffer;
	public int totalPassCount;
	public bool isFirstPass;
	public int passCount;
	public int index;
	public int QuadListIndexCount;
	public Terrain terrain;
	public TerrainData terrainData;
	public TerrainSettings settings;

	public TerrainQuadtreeCullData(Vector3 viewPosition, CullingPlanes cullingPlanes, ResourceHandle<GraphicsBuffer> indirectArgsBuffer, int totalPassCount, bool isFirstPass, int passCount, int index, int quadListIndexCount, Terrain terrain, TerrainData terrainData, TerrainSettings settings)
	{
		this.viewPosition = viewPosition;
		this.cullingPlanes = cullingPlanes;
		this.indirectArgsBuffer = indirectArgsBuffer;
		this.totalPassCount = totalPassCount;
		this.isFirstPass = isFirstPass;
		this.passCount = passCount;
		this.index = index;
		QuadListIndexCount = quadListIndexCount;
		this.terrain = terrain;
		this.terrainData = terrainData;
		this.settings = settings;
	}

	public override bool Equals(object obj) => obj is TerrainQuadtreeCullData other && viewPosition.Equals(other.viewPosition) && EqualityComparer<CullingPlanes>.Default.Equals(cullingPlanes, other.cullingPlanes) && EqualityComparer<ResourceHandle<GraphicsBuffer>>.Default.Equals(indirectArgsBuffer, other.indirectArgsBuffer) && totalPassCount == other.totalPassCount && isFirstPass == other.isFirstPass && passCount == other.passCount && index == other.index && QuadListIndexCount == other.QuadListIndexCount && EqualityComparer<Terrain>.Default.Equals(terrain, other.terrain) && EqualityComparer<TerrainData>.Default.Equals(terrainData, other.terrainData) && EqualityComparer<TerrainSettings>.Default.Equals(settings, other.settings);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(viewPosition);
		hash.Add(cullingPlanes);
		hash.Add(indirectArgsBuffer);
		hash.Add(totalPassCount);
		hash.Add(isFirstPass);
		hash.Add(passCount);
		hash.Add(index);
		hash.Add(QuadListIndexCount);
		hash.Add(terrain);
		hash.Add(terrainData);
		hash.Add(settings);
		return hash.ToHashCode();
	}

	public void Deconstruct(out Vector3 viewPosition, out CullingPlanes cullingPlanes, out ResourceHandle<GraphicsBuffer> indirectArgsBuffer, out int totalPassCount, out bool isFirstPass, out int passCount, out int index, out int quadListIndexCount, out Terrain terrain, out TerrainData terrainData, out TerrainSettings settings)
	{
		viewPosition = this.viewPosition;
		cullingPlanes = this.cullingPlanes;
		indirectArgsBuffer = this.indirectArgsBuffer;
		totalPassCount = this.totalPassCount;
		isFirstPass = this.isFirstPass;
		passCount = this.passCount;
		index = this.index;
		quadListIndexCount = QuadListIndexCount;
		terrain = this.terrain;
		terrainData = this.terrainData;
		settings = this.settings;
	}

	public static implicit operator (Vector3 viewPosition, CullingPlanes cullingPlanes, ResourceHandle<GraphicsBuffer> indirectArgsBuffer, int totalPassCount, bool isFirstPass, int passCount, int index, int QuadListIndexCount, Terrain terrain, TerrainData terrainData, TerrainSettings settings)(TerrainQuadtreeCullData value) => (value.viewPosition, value.cullingPlanes, value.indirectArgsBuffer, value.totalPassCount, value.isFirstPass, value.passCount, value.index, value.QuadListIndexCount, value.terrain, value.terrainData, value.settings);
	public static implicit operator TerrainQuadtreeCullData((Vector3 viewPosition, CullingPlanes cullingPlanes, ResourceHandle<GraphicsBuffer> indirectArgsBuffer, int totalPassCount, bool isFirstPass, int passCount, int index, int QuadListIndexCount, Terrain terrain, TerrainData terrainData, TerrainSettings settings) value) => new TerrainQuadtreeCullData(value.viewPosition, value.cullingPlanes, value.indirectArgsBuffer, value.totalPassCount, value.isFirstPass, value.passCount, value.index, value.QuadListIndexCount, value.terrain, value.terrainData, value.settings);
}