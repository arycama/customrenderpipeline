using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

public class GpuDrivenRenderingRender : CameraRenderFeature
{
    private readonly ComputeShader cullingShader, instancePrefixSum, instanceSort, instanceCompaction, instanceIdOffsets, instanceCopyData, instanceClear;

    public GpuDrivenRenderingRender(RenderGraph renderGraph) : base(renderGraph)
    {
        cullingShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceRendererCull");
        instancePrefixSum = Resources.Load<ComputeShader>("GpuInstancedRendering/InstancePrefixSum");
        instanceSort = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceSort");
        instanceCompaction = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCompaction");
        instanceCopyData = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCopyData");
		instanceIdOffsets = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceIdOffsets");
		instanceClear = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceClear");
    }

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
        var viewData = renderGraph.GetResource<ViewData>();

        if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game && camera.cameraType != CameraType.Reflection)
            return;

        var handle = renderGraph.ResourceMap.GetResourceHandle<GpuInstanceBuffersData>();
        if (!renderGraph.ResourceMap.TryGetRenderPassData<GpuInstanceBuffersData>(handle, renderGraph.FrameIndex, out var gpuInstanceBuffers))
            return;

        var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().CullingPlanes;

        var isShadow = false; // TODO: Support?
        var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
        for (var i = 0; i < cullingPlanes.Count; i++)
            cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

        var visibilityPredicates = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Cull"))
        {
            pass.Initialize(cullingShader, 0, gpuInstanceBuffers.totalInstanceCount);

            if (!isShadow)
                pass.AddKeyword("HIZ_ON");

            pass.WriteBuffer("_RendererInstanceIDs", visibilityPredicates);
            pass.ReadBuffer("_InstanceBounds", gpuInstanceBuffers.instanceBoundsBuffer);

            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<HiZMaxDepthData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                pass.SetFloat("_MaxHiZMip", Texture2DExtensions.MipCount(camera.scaledPixelWidth, camera.scaledPixelHeight) - 1);
                pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                pass.SetInt("_InstanceCount", gpuInstanceBuffers.totalInstanceCount);
            });
        }

        var prefixSums = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
        instancePrefixSum.GetThreadGroupSizes(0, gpuInstanceBuffers.totalInstanceCount, out var groupsX);
        var groupSums = renderGraph.GetBuffer((int)groupsX);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Prefix Sum 1"))
        {
            pass.Initialize(instancePrefixSum, 0, gpuInstanceBuffers.totalInstanceCount);
            pass.WriteBuffer("PrefixSumsWrite", prefixSums);
            pass.WriteBuffer("GroupSumsWrite", groupSums);

            pass.ReadBuffer("Input", visibilityPredicates);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", gpuInstanceBuffers.totalInstanceCount);
            });
        }

        // TODO: This only handles a 2048*1024 array for now
        var groupSums1 = renderGraph.GetBuffer((int)groupsX);
		var totalInstanceCountBuffer = renderGraph.GetBuffer(1);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Prefix Sum 2"))
        {
            pass.Initialize(instancePrefixSum, 1, (int)groupsX);
            pass.WriteBuffer("PrefixSumsWrite", groupSums1);
            pass.WriteBuffer("TotalInstanceCount", totalInstanceCountBuffer);
            pass.ReadBuffer("Input", groupSums);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", (int)groupsX);
            });
        }

		// Clear the draw call args buffer
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Clear Instance Counts"))
		{
			var threadCount = gpuInstanceBuffers.totalRendererCount;
			pass.Initialize(instanceClear, 0, threadCount);
			pass.WriteBuffer("Output", gpuInstanceBuffers.drawCallArgsBuffer);

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("MaxThread", threadCount);
			});
		}

		// Need a buffer big enough to hold all potential indices
		var instanceIndices = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
		var lodCounts = renderGraph.GetBuffer(gpuInstanceBuffers.totalLodCount);
        //var sortKeys = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Stream Compaction"))
        {
            pass.Initialize(instanceCompaction, 0, gpuInstanceBuffers.totalInstanceCount);
            pass.WriteBuffer("Output", instanceIndices);
            //pass.WriteBuffer("SortKeysWrite", sortKeys);
			pass.WriteBuffer("DrawCallArgsWrite", gpuInstanceBuffers.drawCallArgsBuffer);
			pass.WriteBuffer("LodCounts", lodCounts);

			pass.ReadBuffer("Input", visibilityPredicates);
            pass.ReadBuffer("PrefixSums", prefixSums);
            pass.ReadBuffer("GroupSums", groupSums1);
            pass.ReadBuffer("InstanceBounds", gpuInstanceBuffers.instanceBoundsBuffer);
			pass.ReadBuffer("InstanceTypeIds", gpuInstanceBuffers.instanceTypeIdsBuffer);
			pass.ReadBuffer("InstanceTypeDatas", gpuInstanceBuffers.instanceTypeDataBuffer);
			pass.ReadBuffer("InstanceTypeLodDatas", gpuInstanceBuffers.instanceTypeLodDataBuffer);

			pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", gpuInstanceBuffers.totalInstanceCount);
                pass.SetVector("CameraForward", camera.transform.forward);
                pass.SetVector("ViewPosition", camera.transform.position);

				using (ArrayPool<uint>.Get(gpuInstanceBuffers.totalLodCount, out var data))
					command.SetBufferData(pass.GetBuffer(lodCounts), data);
            });
        }

		// Write out the offset for each type
		var instanceIdOffsetsBuffer = renderGraph.GetBuffer(gpuInstanceBuffers.totalLodCount);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Id Offsets"))
		{
			pass.Initialize(instanceIdOffsets, 0, 1, 1, 1, false);
			pass.WriteBuffer("Output", instanceIdOffsetsBuffer);
			pass.ReadBuffer("LodCounts", lodCounts);

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("MaxThread", gpuInstanceBuffers.totalLodCount);
			});
		}

		var threadGroups = renderGraph.GetBuffer(6, target: GraphicsBuffer.Target.IndirectArguments);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("ComputeThreadCount"))
        {
            pass.Initialize(instanceCopyData, 0, 1, 1, 1, false);
            pass.WriteBuffer("ThreadGroupsWrite", threadGroups);
            pass.ReadBuffer("TotalInstanceCount", totalInstanceCountBuffer);
        }

        //var sortedInstanceIndices = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
        //var sortedKeys = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
        //using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Instance Sort"))
        //{
        //    pass.Initialize(instanceSort, threadGroups, 0, 3);
        //    pass.WriteBuffer("Result", sortedInstanceIndices);
        //    pass.WriteBuffer("SortKeysWrite", sortedKeys);

        //    pass.ReadBuffer("Input", instanceIndices);
        //    pass.ReadBuffer("SortKeys", sortKeys);
        //    pass.ReadBuffer("TotalInstanceCount", totalInstanceCountBuffer);
        //}

        var objectToWorld = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount, UnsafeUtility.SizeOf<Float3x4>());
        using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Copy Data"))
        {
            pass.Initialize(instanceCopyData, threadGroups, 1);
            pass.WriteBuffer("_ObjectToWorldWrite", objectToWorld);

            pass.ReadBuffer("InputIndices", instanceIndices);
            pass.ReadBuffer("_Positions", gpuInstanceBuffers.positionsBuffer);
            pass.ReadBuffer("TotalInstanceCount", totalInstanceCountBuffer);

            // Just here for debug
           // pass.ReadBuffer("SortedKeys", sortedKeys);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", gpuInstanceBuffers.totalInstanceCount);
            });
        }

        if (!gpuInstanceBuffers.rendererDrawCallData.TryGetValue("MotionVectors", out var drawList))
            return;

		// Render instances
		for (var i = 0; i < drawList.Count; i++)
        {
			var draw = drawList[i];
			using (var pass = renderGraph.AddRenderPass<DrawInstancedIndirectRenderPass>("Gpu Driven Rendering"))
            {
                pass.Initialize(draw.mesh, draw.submeshIndex, draw.material, gpuInstanceBuffers.drawCallArgsBuffer, draw.passIndex, "INDIRECT_RENDERING", 0.0f, 0.0f, true, draw.indirectArgsOffset);

                pass.WriteDepth(renderGraph.GetResource<CameraDepthData>());
                pass.WriteTexture(renderGraph.GetResource<AlbedoMetallicData>());
                pass.WriteTexture(renderGraph.GetResource<NormalRoughnessData>());
                pass.WriteTexture(renderGraph.GetResource<BentNormalOcclusionData>());
                pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
                pass.WriteTexture(renderGraph.GetResource<TranslucencyData>());
                pass.WriteTexture(renderGraph.GetResource<VelocityData>());
                pass.AddRenderPassData<AutoExposureData>();
                pass.AddRenderPassData<TemporalAAData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<ViewData>();
                pass.AddRenderPassData<FrameData>();

                pass.ReadBuffer("_VisibleRendererInstanceIndices", visibilityPredicates);
                pass.ReadBuffer("_ObjectToWorld", objectToWorld);
                pass.ReadBuffer("_InstancePositions", gpuInstanceBuffers.positionsBuffer);
                pass.ReadBuffer("_InstanceLodFades", gpuInstanceBuffers.lodFadesBuffer);
				pass.ReadBuffer("InstanceIdOffsets", instanceIdOffsetsBuffer);

				pass.SetRenderFunction(draw.lodOffset, static (command, pass, lodOffset) =>
				{
					pass.SetInt("InstanceIdOffsetsIndex", lodOffset);
				});
            }
        }
    }
}