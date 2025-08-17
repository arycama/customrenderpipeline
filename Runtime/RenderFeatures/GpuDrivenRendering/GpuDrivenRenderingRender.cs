using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

public class GpuDrivenRenderingRender : CameraRenderFeature
{
    private readonly ComputeShader cullingShader, instancePrefixSum, instanceSort, instanceCompaction, instanceIdOffsets, instanceCopyData, writeDrawCallArgs;

    public GpuDrivenRenderingRender(RenderGraph renderGraph) : base(renderGraph)
    {
        cullingShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceRendererCull");
        instancePrefixSum = Resources.Load<ComputeShader>("GpuInstancedRendering/InstancePrefixSum");
        instanceSort = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceSort");
        instanceCompaction = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCompaction");
        instanceCopyData = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCopyData");
		instanceIdOffsets = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceIdOffsets");
		writeDrawCallArgs = Resources.Load<ComputeShader>("GpuInstancedRendering/WriteDrawCallArgs");
    }

    public override void Render(Camera camera, ScriptableRenderContext context)
    {
        var viewData = renderGraph.GetResource<ViewData>();

        if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game && camera.cameraType != CameraType.Reflection)
            return;

        var handle = renderGraph.ResourceMap.GetResourceHandle<GpuDrivenRenderingData>();
        if (!renderGraph.ResourceMap.TryGetRenderPassData<GpuDrivenRenderingData>(handle, renderGraph.FrameIndex, out var gpuInstanceBuffers))
            return;

        var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().CullingPlanes;

        var isShadow = false; // TODO: Support?
        var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
        for (var i = 0; i < cullingPlanes.Count; i++)
            cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

        var visibilityPredicates = renderGraph.GetBuffer(gpuInstanceBuffers.instanceCount);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Cull"))
        {
            pass.Initialize(cullingShader, 0, gpuInstanceBuffers.instanceCount);

            if (!isShadow)
                pass.AddKeyword("HIZ_ON");

            pass.WriteBuffer("_RendererInstanceIDs", visibilityPredicates);
            pass.ReadBuffer("_InstanceBounds", gpuInstanceBuffers.instanceBounds);

            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<HiZMaxDepthData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                pass.SetFloat("_MaxHiZMip", Texture2DExtensions.MipCount(camera.scaledPixelWidth, camera.scaledPixelHeight) - 1);
                pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                pass.SetInt("_InstanceCount", gpuInstanceBuffers.instanceCount);
            });
        }

        var prefixSums = renderGraph.GetBuffer(gpuInstanceBuffers.instanceCount);
        instancePrefixSum.GetThreadGroupSizes(0, gpuInstanceBuffers.instanceCount, out var groupsX);
        var groupSums = renderGraph.GetBuffer((int)groupsX);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Prefix Sum 1"))
        {
            pass.Initialize(instancePrefixSum, 0, gpuInstanceBuffers.instanceCount);
            pass.WriteBuffer("PrefixSumsWrite", prefixSums);
            pass.WriteBuffer("GroupSumsWrite", groupSums);

            pass.ReadBuffer("Input", visibilityPredicates);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", gpuInstanceBuffers.instanceCount);
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

		// Need a buffer big enough to hold all potential indices
		var instanceIndices = renderGraph.GetBuffer(gpuInstanceBuffers.instanceCount);
		var lodCounts = renderGraph.GetBuffer(gpuInstanceBuffers.lodCount);
        //var sortKeys = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Stream Compaction"))
        {
            pass.Initialize(instanceCompaction, 0, gpuInstanceBuffers.instanceCount);
            pass.WriteBuffer("Output", instanceIndices);
            //pass.WriteBuffer("SortKeysWrite", sortKeys);
			pass.WriteBuffer("LodCounts", lodCounts);

			pass.ReadBuffer("Input", visibilityPredicates);
            pass.ReadBuffer("PrefixSums", prefixSums);
            pass.ReadBuffer("GroupSums", groupSums1);
            pass.ReadBuffer("InstanceBounds", gpuInstanceBuffers.instanceBounds);
			pass.ReadBuffer("InstanceTypeIds", gpuInstanceBuffers.instanceTypes);
			pass.ReadBuffer("InstanceTypeDatas", gpuInstanceBuffers.instanceTypeData);

			pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", gpuInstanceBuffers.instanceCount);
                pass.SetVector("CameraForward", camera.transform.forward);
                pass.SetVector("ViewPosition", camera.transform.position);

				using (ArrayPool<uint>.Get(gpuInstanceBuffers.lodCount, out var data))
					command.SetBufferData(pass.GetBuffer(lodCounts), data);
            });
        }

		// Write the draw call args for each type
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Write Draw Call Args"))
		{
			pass.Initialize(writeDrawCallArgs, 0, gpuInstanceBuffers.rendererCount);
			pass.WriteBuffer("DrawCallArgs", gpuInstanceBuffers.drawCallArgs);

			// Contains mapping from renderer to lod, which is used to pull the lod count for each renderer, as one lod may have multiple renderers, submeshes etc
			pass.ReadBuffer("RendererLodIndices", gpuInstanceBuffers.rendererLodIndices);
			pass.ReadBuffer("LodCounts", lodCounts);

			pass.SetRenderFunction((command, pass) =>
			{
				var b = pass.GetBuffer(gpuInstanceBuffers.rendererLodIndices);

				pass.SetInt("MaxThread", gpuInstanceBuffers.rendererCount);
			});
		}

		// Write out the offset for each type
		var instanceIdOffsetsBuffer = renderGraph.GetBuffer(gpuInstanceBuffers.lodCount);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Id Offsets"))
		{
			pass.Initialize(instanceIdOffsets, 0, 1, 1, 1, false);
			pass.WriteBuffer("Output", instanceIdOffsetsBuffer);
			pass.ReadBuffer("LodCounts", lodCounts);

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("MaxThread", gpuInstanceBuffers.lodCount);
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

        var objectToWorld = renderGraph.GetBuffer(gpuInstanceBuffers.instanceCount, UnsafeUtility.SizeOf<Float3x4>());
        using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Copy Data"))
        {
            pass.Initialize(instanceCopyData, threadGroups, 1);
            pass.WriteBuffer("_ObjectToWorldWrite", objectToWorld);

            pass.ReadBuffer("InputIndices", instanceIndices);
            pass.ReadBuffer("_Positions", gpuInstanceBuffers.positions);
            pass.ReadBuffer("TotalInstanceCount", totalInstanceCountBuffer);

            // Just here for debug
           // pass.ReadBuffer("SortedKeys", sortedKeys);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", gpuInstanceBuffers.instanceCount);
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
                pass.Initialize(draw.mesh, draw.submeshIndex, draw.material, gpuInstanceBuffers.drawCallArgs, draw.passIndex, "INDIRECT_RENDERING", 0.0f, 0.0f, true, draw.indirectArgsOffset);

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
                pass.ReadBuffer("_InstancePositions", gpuInstanceBuffers.positions);
                pass.ReadBuffer("_InstanceLodFades", gpuInstanceBuffers.lodFades);
				pass.ReadBuffer("InstanceIdOffsets", instanceIdOffsetsBuffer);

				pass.SetRenderFunction((draw.lodOffset, draw.objectToWorld), static (command, pass, data) =>
				{
					pass.SetInt("InstanceIdOffsetsIndex", data.lodOffset);
					pass.SetMatrix("LocalToWorld", (Matrix4x4)data.objectToWorld);
				});
            }
        }
    }
}