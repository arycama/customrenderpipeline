using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public class GpuDrivenRenderer : RenderFeatureBase
{
	private static IndexedString radixPassId = new("Pass ", 32);
    private readonly ComputeShader cullingShader, instancePrefixSum, instanceCompaction, instanceSort, instanceIdOffsets, instanceCopyData, writeDrawCallArgs;

    public GpuDrivenRenderer(RenderGraph renderGraph) : base(renderGraph)
    {
        cullingShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceRendererCull");
        instancePrefixSum = Resources.Load<ComputeShader>("GpuInstancedRendering/InstancePrefixSum");
        instanceSort = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceSort");
		instanceCompaction = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCompaction");
        instanceCopyData = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCopyData");
		instanceIdOffsets = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceIdOffsets");
		writeDrawCallArgs = Resources.Load<ComputeShader>("GpuInstancedRendering/WriteDrawCallArgs");
    }

    public GpuRenderingData Setup(Int2 viewSize, bool isShadow, CullingPlanes cullingPlanes, GpuDrivenRenderingData instanceData)
    {
		using var scope = renderGraph.AddProfileScope("Setup");

		var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
		for (var i = 0; i < cullingPlanes.Count; i++)
			cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

		var visibilityPredicates = renderGraph.GetBuffer(instanceData.instanceCount);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Cull"))
        {
            pass.Initialize(cullingShader, 0, instanceData.instanceCount);

            if (!isShadow)
                pass.AddKeyword("HIZ_ON");

            pass.WriteBuffer("_RendererInstanceIDs", visibilityPredicates);
            pass.ReadBuffer("_InstanceBounds", instanceData.instanceBounds);

            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<HiZMaxDepthData>();

			var maxMip = Texture2DExtensions.MipCount(viewSize) - 1;
			pass.SetRenderFunction((cullingPlanesArray, maxMip, instanceData.instanceCount), static (command, pass, data) =>
            {
                pass.SetVectorArray("_CullingPlanes", data.cullingPlanesArray);
                pass.SetFloat("_MaxHiZMip", data.maxMip);
                pass.SetInt("_CullingPlanesCount", data.cullingPlanesArray.Length);
                pass.SetInt("_InstanceCount", data.instanceCount);

				ArrayPool<Vector4>.Release(data.cullingPlanesArray);
            });
        }

        var prefixSums = renderGraph.GetBuffer(instanceData.instanceCount);

		// We use 1024 thread groups but each thread reads two sums
		instancePrefixSum.GetThreadGroupSizes(0, instanceData.instanceCount, out var groups);
		var groupSums = renderGraph.GetBuffer((int)groups);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Prefix Sum 1"))
        {
            pass.Initialize(instancePrefixSum, 0, (int)groups, normalizedDispatch: false);
            pass.WriteBuffer("PrefixSumsWrite", prefixSums);
            pass.WriteBuffer("GroupSumsWrite", groupSums);

            pass.ReadBuffer("Input", visibilityPredicates);

            pass.SetRenderFunction(instanceData.instanceCount, static (command, pass, instanceCount) =>
            {
                pass.SetInt("MaxThread", instanceCount);
            });
        }

        // TODO: This only handles a 2048*1024 array for now
        var groupSums1 = renderGraph.GetBuffer((int)groups);
		var totalInstanceCountBuffer = renderGraph.GetBuffer(4, target: GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Prefix Sum 2"))
        {
            pass.Initialize(instancePrefixSum, 1, 1, normalizedDispatch: false);
            pass.WriteBuffer("PrefixSumsWrite", groupSums1);
            pass.WriteBuffer("TotalInstanceCount", totalInstanceCountBuffer);
            pass.ReadBuffer("Input", groupSums);

            pass.SetRenderFunction(groups, static (command, pass, groups) =>
            {
                pass.SetInt("MaxThread", (int)groups);
            });
        }

		var maxCount = renderGraph.GetBuffer(4, target: GraphicsBuffer.Target.Constant | GraphicsBuffer.Target.CopyDestination);
		using (var pass = renderGraph.AddRenderPass<GenericRenderPass>("Copy Count"))
		{
			pass.WriteBuffer("", maxCount);
			pass.ReadBuffer("", totalInstanceCountBuffer);
			pass.SetRenderFunction((maxCount, totalInstanceCountBuffer), static (command, pass, data) =>
			{
				command.CopyBuffer(pass.GetBuffer(data.totalInstanceCountBuffer), pass.GetBuffer(data.maxCount));
			});
		}

		// Need a buffer big enough to hold all potential indices
		var instanceIndices = renderGraph.GetBuffer(instanceData.instanceCount);
        var sortKeys = renderGraph.GetBuffer(instanceData.instanceCount);
		var lodCounts = renderGraph.GetBuffer(instanceData.lodCount);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Stream Compaction"))
        {
            pass.Initialize(instanceCompaction, 0, instanceData.instanceCount);
            pass.WriteBuffer("Output", instanceIndices);
			pass.WriteBuffer("LodCounts", lodCounts);
			pass.WriteBuffer("SortKeysWrite", sortKeys);

			pass.ReadBuffer("Input", visibilityPredicates);
            pass.ReadBuffer("PrefixSums", prefixSums);
            pass.ReadBuffer("GroupSums", groupSums1);
            pass.ReadBuffer("InstanceBounds", instanceData.instanceBounds);
			pass.ReadBuffer("InstanceTypeIds", instanceData.instanceTypes);
			pass.ReadBuffer("InstanceTypeDatas", instanceData.instanceTypeData);
			pass.ReadBuffer("LodSizes", instanceData.lodSizes);
			pass.ReadBuffer("InstanceMatrices", instanceData.positions);

			pass.AddRenderPassData<ViewData>();

			var lodCountsData = ArrayPool<uint>.Get(instanceData.lodCount);
			for (var i = 0; i < instanceData.lodCount; i++)
				lodCountsData[i] = 0;

			pass.SetRenderFunction((instanceData.instanceCount, lodCounts, lodCountsData), static (command, pass, data) =>
            {
                pass.SetInt("MaxThread", data.instanceCount);
				command.SetBufferData(pass.GetBuffer(data.lodCounts), data.lodCountsData);
				ArrayPool<uint>.Release(data.lodCountsData);
            });
        }

		// Write the draw call args for each type
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Write Draw Call Args"))
		{
			pass.Initialize(writeDrawCallArgs, 0, instanceData.rendererCount);
			pass.WriteBuffer("DrawCallArgs", instanceData.drawCallArgs);

			// Contains mapping from renderer to lod, which is used to pull the lod count for each renderer, as one lod may have multiple renderers, submeshes etc
			pass.ReadBuffer("RendererLodIndices", instanceData.rendererLodIndices);
			pass.ReadBuffer("LodCounts", lodCounts);

			pass.SetRenderFunction(instanceData.rendererCount, static (command, pass, rendererCount) =>
			{
				pass.SetInt("MaxThread", rendererCount);
			});
		}

		// Write out the offset for each type
		var instanceIdOffsetsBuffer = renderGraph.GetBuffer(instanceData.lodCount);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Id Offsets"))
		{
			pass.Initialize(instanceIdOffsets, 0, 1, 1, 1, false);
			pass.WriteBuffer("Output", instanceIdOffsetsBuffer);
			pass.ReadBuffer("LodCounts", lodCounts);

			pass.SetRenderFunction(instanceData.lodCount, static (command, pass, lodCount) =>
			{
				pass.SetInt("MaxThread", lodCount);
			});
		}

		var threadGroups = renderGraph.GetBuffer(6, target: GraphicsBuffer.Target.IndirectArguments);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("ComputeThreadCount"))
        {
            pass.Initialize(instanceCopyData, 0, 1, 1, 1, false);
            pass.WriteBuffer("ThreadGroupsWrite", threadGroups);
            pass.ReadBuffer("DataLength", maxCount);
        }

		// Radix sort zzz too many passes
		using (renderGraph.AddProfileScope("Radix Sort"))
		{
			instanceSort.GetThreadGroupSizes(0, instanceData.instanceCount, out var countGroups);
			var tempKeys = renderGraph.GetBuffer(instanceData.instanceCount);
			var tempData = renderGraph.GetBuffer(instanceData.instanceCount);
			var countResult = renderGraph.GetBuffer((int)countGroups, sizeof(int));
			var scanResult = renderGraph.GetBuffer((int)countGroups * 4, sizeof(int));
			var scanSums = renderGraph.GetBuffer(16); // Stores total sums for each value, needs to be 2^n, where n is bits per pass

			var bitsPerPass = 2;
			var totalBits = 32;
			for (var i = 0; i < totalBits; i += bitsPerPass)
			{
				using var passScope = renderGraph.AddProfileScope(radixPassId[i]);
				using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Radix Count"))
				{
					pass.Initialize(instanceSort, threadGroups, 0);
					pass.WriteBuffer("KeysResult", tempKeys);
					pass.WriteBuffer("DataResult", tempData);
					pass.WriteBuffer("CountResult", countResult);

					pass.ReadBuffer("DataLength", maxCount);
					pass.ReadBuffer("Keys", sortKeys);
					pass.ReadBuffer("Data", instanceIndices);

					pass.SetRenderFunction(i, static (command, pass, data) => { pass.SetInt("BitIndex", data); });
				}

				using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Radix Sum"))
				{
					pass.Initialize(instanceSort, 1, 4, normalizedDispatch: false);
					pass.WriteBuffer("ScanResult", scanResult);
					pass.WriteBuffer("TotalSumsResult", scanSums);
					pass.ReadBuffer("GroupCounts", countResult);
					pass.ReadBuffer("TotalGroupCount", threadGroups);
				}
			
				using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Radix Scatter"))
				{
					pass.Initialize(instanceSort, threadGroups, 2);
					pass.WriteBuffer("KeysResult", sortKeys);
					pass.WriteBuffer("DataResult", instanceIndices);

					pass.ReadBuffer("DataLength", maxCount);
					pass.ReadBuffer("Keys", tempKeys);
					pass.ReadBuffer("Data", tempData);

					pass.ReadBuffer("GroupScans", scanResult);
					pass.ReadBuffer("GroupCounts", countResult);
					pass.ReadBuffer("TotalSums", scanSums);

					pass.SetRenderFunction(i, static (command, pass, data) => { pass.SetInt("BitIndex", data); });
				}
			}
		}

		var objectToWorld = renderGraph.GetBuffer(instanceData.instanceCount, UnsafeUtility.SizeOf<Float3x4>());
        using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Copy Data"))
        {
            pass.Initialize(instanceCopyData, threadGroups, 1);
            pass.WriteBuffer("_ObjectToWorldWrite", objectToWorld);

            pass.ReadBuffer("InputIndices", instanceIndices);
            pass.ReadBuffer("_Positions", instanceData.positions);
			pass.ReadBuffer("DataLength", maxCount);

			// Temporarily here to avoid memory leaks due to sortedKeys not being used anywhere. (Only used for debug really)
			pass.ReadBuffer("SortKeys", sortKeys);

			pass.SetRenderFunction(instanceData.instanceCount, static (command, pass, instanceCount) =>
            {
                pass.SetInt("MaxThread", instanceCount);
            });
        }

		return new GpuRenderingData(visibilityPredicates, objectToWorld, instanceIdOffsetsBuffer);
    }

	public void RenderShadow(Float3 mainViewPosition, ShadowRequestData request, Int2 viewSize)
	{
		var handle = renderGraph.ResourceMap.GetResourceHandle<GpuDrivenRenderingData>();
		if (!renderGraph.ResourceMap.TryGetRenderPassData<GpuDrivenRenderingData>(handle, renderGraph.FrameIndex, out var instanceData))
			return;

		if (!instanceData.rendererDrawCallData.TryGetValue("ShadowCaster", out var drawList))
			return;

		var cullingPlanes = new CullingPlanes() { Count = request.ShadowRequest.ShadowSplitData.cullingPlaneCount };
		for (var i = 0; i < cullingPlanes.Count; i++)
		{
			var plane = request.ShadowRequest.ShadowSplitData.GetCullingPlane(i);
			plane.Translate(mainViewPosition);
			cullingPlanes.SetCullingPlane(i, plane);
		}

		using var setupScope = renderGraph.AddProfileScope("Gpu Driven Rendering");

		var renderingData = Setup(viewSize, true, cullingPlanes, instanceData);

		using var renderScope = renderGraph.AddProfileScope("Render");

		for (var i = 0; i < drawList.Count; i++)
		{
			var draw = drawList[i];
			using (var pass = renderGraph.AddRenderPass<DrawInstancedIndirectRenderPass>("Gpu Driven Rendering Shadow"))
			{
				pass.UseProfiler = false;
				pass.Initialize(draw.mesh, draw.submeshIndex, draw.material, instanceData.drawCallArgs, draw.passIndex, "INDIRECT_RENDERING", request.Bias, request.SlopeBias, request.ZClip, draw.indirectArgsOffset);

				pass.WriteDepth(request.Shadow);
				pass.DepthSlice = request.CascadeIndex;

				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();
				pass.AddRenderPassData<ShadowRequestData>();

				pass.ReadBuffer("_VisibleRendererInstanceIndices", renderingData.visibilityPredicates);
				pass.ReadBuffer("_ObjectToWorld", renderingData.objectToWorld);
				pass.ReadBuffer("_InstancePositions", instanceData.positions);
				pass.ReadBuffer("_InstanceLodFades", instanceData.lodFades);
				pass.ReadBuffer("InstanceIdOffsets", renderingData.instanceIdOffsetsBuffer);

				pass.SetRenderFunction((draw.lodOffset, draw.objectToWorld), static (command, pass, data) =>
				{
					pass.SetInt("InstanceIdOffsetsIndex", data.lodOffset);
					pass.SetMatrix("LocalToWorld", (Matrix4x4)data.objectToWorld);
				});
			}
		}
	}
}
