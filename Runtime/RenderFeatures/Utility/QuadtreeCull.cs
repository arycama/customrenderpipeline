using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;

public class QuadtreeCull
{
	private readonly RenderGraph renderGraph;
	private readonly ComputeShader computeShader;

	public QuadtreeCull(RenderGraph renderGraph)
	{
		this.renderGraph = renderGraph;
		computeShader = Resources.Load<ComputeShader>("Utility/QuadtreeCull");
	}

	public QuadtreeCullResult Cull(int cellCount, CullingPlanes cullingPlanes, int indexCountPerInstance, float edgeLength, Float4 positionOffset, bool useHiZ, Int2 viewSize, bool useMinMaxHeights, ResourceHandle<RenderTexture> minMaxHeights = default, float heightScale = 0f, float heightOffset = 0f, int maxHeightMips = 0, float maxHeightOffset = 0)
	{
		var indirectArgsBuffer = renderGraph.GetBuffer(5, target: GraphicsBuffer.Target.IndirectArguments);

		// Set the initial buffer contents
		using (var pass = renderGraph.AddGenericRenderPass("Set Indirect Data", (indexCountPerInstance, indirectArgsBuffer)))
		{
			pass.WriteBuffer("", indirectArgsBuffer);
			pass.SetRenderFunction(static (command, pass, data) =>
			{
				var indirectArgs = ListPool<int>.Get();
				indirectArgs.Add(data.indexCountPerInstance); // index count per instance
				indirectArgs.Add(0); // instance count (filled in later)
				indirectArgs.Add(0); // start index location
				indirectArgs.Add(0); // base vertex location
				indirectArgs.Add(0); // start instance location
				command.SetBufferData(pass.GetBuffer(data.indirectArgsBuffer), indirectArgs);
				ListPool<int>.Release(indirectArgs);
			});
		}

		// We can do 32x32 cells in a single pass, larger counts need to be broken up into several passes
		var maxPassesPerDispatch = 6;
		var totalPassCount = (int)Math.Log2(cellCount) + 1;
		var dispatchCount = Math.DivRoundUp(totalPassCount, maxPassesPerDispatch);

		ResourceHandle<RenderTexture> tempLodId = default;
		ResourceHandle<GraphicsBuffer> lodIndirectArgsBuffer = default;
		if (dispatchCount > 1)
		{
			// If more than one dispatch, we need to write lods out to a temp texture first. Otherwise they are done via shared memory so no texture is needed
			tempLodId = renderGraph.GetTexture(cellCount, GraphicsFormat.R16_UInt);
			lodIndirectArgsBuffer = renderGraph.GetBuffer(3, target: GraphicsBuffer.Target.IndirectArguments);
		}

		var tempIds = ListPool<ResourceHandle<RenderTexture>>.Get();
		for (var i = 0; i < dispatchCount - 1; i++)
		{
			var tempResolution = 1 << ((i + 1) * (maxPassesPerDispatch - 1));
			tempIds.Add(renderGraph.GetTexture(tempResolution, GraphicsFormat.R16_UInt));
		}

		var patchDataBuffer = renderGraph.GetBuffer(cellCount * cellCount, target: GraphicsBuffer.Target.Structured);
		for (var i = 0; i < dispatchCount; i++)
		{
			var passCount = Mathf.Min(maxPassesPerDispatch, totalPassCount - i * maxPassesPerDispatch);
			var index = i;
			var maxMip = Texture2DExtensions.MipCount(viewSize) - 1;

			using (var pass = renderGraph.AddComputeRenderPass("Terrain Quadtree Cull", new QuadtreeCullData
			(
				cullingPlanes,
				totalPassCount,
				passCount,
				index,
				edgeLength,
				heightScale,
				heightOffset,
				positionOffset,
				maxHeightMips,
				maxMip,
				maxHeightOffset
			)))
			{
				var isFirstPass = i == 0; // Also indicates whether this is -not- the first pass
				var isFinalPass = i == dispatchCount - 1; // Also indicates whether this is -not- the final pass
				var threadCount = 1 << (i * 6 + passCount - 1);

				pass.Initialize(computeShader, 0, threadCount, threadCount);

				pass.WriteBuffer("_IndirectArgs", indirectArgsBuffer);
				pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);

				pass.ReadResource<ViewData>();

				if (!isFirstPass)
					pass.ReadTexture("_TempResult", tempIds[i - 1]);

				if (isFirstPass)
					pass.AddKeyword("FIRST");

				if (isFinalPass)
					pass.AddKeyword("FINAL");

				// Final pass writes out lods to a temp texture if more than one pass was used
				if (isFinalPass && !isFirstPass)
					pass.WriteTexture("_LodResult", tempLodId);

				if (!isFinalPass)
					pass.WriteTexture("_TempResultWrite", tempIds[i]);

				if (useMinMaxHeights)
				{
					pass.AddKeyword("USE_TERRAIN_HEIGHT");
					pass.ReadTexture("_TerrainHeights", minMaxHeights);
				}

				if(useHiZ)
				{
					pass.AddKeyword("HIZ_ON");
					pass.ReadRtHandle<HiZMaxDepth>();
				}

				pass.SetRenderFunction(static (command, pass, data) =>
				{
					// Do up to 6 passes per dispatch.
					pass.SetFloat("_EdgeLength", data.edgeLength);
					pass.SetFloat("_InputOffset", data.heightOffset);
					pass.SetFloat("_InputScale", data.heightScale);
					pass.SetFloat("_MaxHiZMip", data.maxMip);
					pass.SetFloat("MaxHeightOffset", data.maxHeightOffset);
					pass.SetInt("_MipCount", data.maxHeightMips);
					pass.SetInt("_PassCount", data.passCount);
					pass.SetInt("_PassOffset", 6 * data.index);
					pass.SetInt("_TotalPassCount", data.totalPassCount);
					pass.SetVector("_TerrainPositionOffset", data.positionOffset);

					pass.SetInt("_CullingPlanesCount", data.cullingPlanes.Count);
					var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
					for (var i = 0; i < data.cullingPlanes.Count; i++)
						cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

					pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
					ArrayPool<Vector4>.Release(cullingPlanesArray);
				});
			}
		}

		if (dispatchCount > 1)
		{
			using (var pass = renderGraph.AddComputeRenderPass("Terrain Quadtree Cull"))
			{
				pass.Initialize(computeShader, 1, normalizedDispatch: false);
				pass.WriteBuffer("_IndirectArgs", lodIndirectArgsBuffer);

				// If more than one pass needed, we need a second pass to write out lod deltas to the patch data
				// Copy count from indirect draw args so we only dispatch as many threads as needed
				pass.ReadBuffer("_IndirectArgsInput", indirectArgsBuffer);
			}

			using (var pass = renderGraph.AddIndirectComputeRenderPass("Terrain Quadtree Cull", cellCount))
			{
				pass.Initialize(computeShader, lodIndirectArgsBuffer, 2);
				pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
				pass.ReadTexture("_LodInput", tempLodId);
				pass.ReadBuffer("_IndirectArgs", indirectArgsBuffer);

				pass.SetRenderFunction(static (command, pass, cellCount) =>
				{
					pass.SetInt("_CellCount", cellCount);
				});
			}
		}

		ListPool<ResourceHandle<RenderTexture>>.Release(tempIds);

		return new(indirectArgsBuffer, patchDataBuffer);
	}

	private readonly struct QuadtreeCullData
	{
		public readonly CullingPlanes cullingPlanes;
		public readonly int totalPassCount;
		public readonly int passCount;
		public readonly int index;
		public readonly float edgeLength;
		public readonly float heightScale;
		public readonly float heightOffset;
		public readonly Float4 positionOffset;
		public readonly int maxHeightMips;
		public readonly int maxMip;
		public readonly float maxHeightOffset;

		public QuadtreeCullData(CullingPlanes cullingPlanes, int totalPassCount, int passCount, int index, float edgeLength, float heightScale, float heightOffset, Float4 positionOffset, int maxHeightMips, int maxMip, float maxHeightOffset)
		{
			this.cullingPlanes = cullingPlanes;
			this.totalPassCount = totalPassCount;
			this.passCount = passCount;
			this.index = index;
			this.edgeLength = edgeLength;
			this.heightScale = heightScale;
			this.heightOffset = heightOffset;
			this.positionOffset = positionOffset;
			this.maxHeightMips = maxHeightMips;
			this.maxMip = maxMip;
			this.maxHeightOffset = maxHeightOffset;
		}
	}
}

public readonly struct QuadtreeCullResult
{
	public ResourceHandle<GraphicsBuffer> IndirectArgsBuffer { get; }
	public ResourceHandle<GraphicsBuffer> PatchDataBuffer { get; }

	public QuadtreeCullResult(ResourceHandle<GraphicsBuffer> indirectArgsBuffer, ResourceHandle<GraphicsBuffer> patchDataBuffer)
	{
		IndirectArgsBuffer = indirectArgsBuffer;
		PatchDataBuffer = patchDataBuffer;
	}
}

