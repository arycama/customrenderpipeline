using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct GpuRenderingData
{
	public readonly ResourceHandle<GraphicsBuffer> visibilityPredicates;
	public readonly ResourceHandle<GraphicsBuffer> objectToWorld;
	public readonly ResourceHandle<GraphicsBuffer> instanceIdOffsetsBuffer;

	public GpuRenderingData(ResourceHandle<GraphicsBuffer> visibilityPredicates, ResourceHandle<GraphicsBuffer> objectToWorld, ResourceHandle<GraphicsBuffer> instanceIdOffsetsBuffer)
	{
		this.visibilityPredicates = visibilityPredicates;
		this.objectToWorld = objectToWorld;
		this.instanceIdOffsetsBuffer = instanceIdOffsetsBuffer;
	}
}

public class GpuDrivenRenderer : RenderFeatureBase
{
    private readonly ComputeShader cullingShader, instancePrefixSum, instanceCompaction, instanceIdOffsets, instanceCopyData, writeDrawCallArgs;

    public GpuDrivenRenderer(RenderGraph renderGraph) : base(renderGraph)
    {
        cullingShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceRendererCull");
        instancePrefixSum = Resources.Load<ComputeShader>("GpuInstancedRendering/InstancePrefixSum");
        instanceCompaction = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCompaction");
        instanceCopyData = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCopyData");
		instanceIdOffsets = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceIdOffsets");
		writeDrawCallArgs = Resources.Load<ComputeShader>("GpuInstancedRendering/WriteDrawCallArgs");
    }

    public GpuRenderingData Render(Int2 viewSize, Float3 mainViewPosition, Float3 mainViewForward, bool isShadow, CullingPlanes cullingPlanes, GpuDrivenRenderingData instanceData)
    {
		using var scope = renderGraph.AddProfileScope("Gpu Driven Rendering");

		var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
		for (var i = 0; i < cullingPlanes.Count; i++)
			cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

		var visibilityPredicates = renderGraph.GetBuffer(instanceData.instanceCount);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Cull"))
        {
            pass.Initialize(cullingShader, 0, instanceData.instanceCount);

            if (!isShadow)
                pass.AddKeyword("HIZ_ON");

            pass.WriteBuffer("_RendererInstanceIDs", visibilityPredicates);
            pass.ReadBuffer("_InstanceBounds", instanceData.instanceBounds);

            pass.AddRenderPassData<FrameData>();
            pass.AddRenderPassData<ViewData>();
            pass.AddRenderPassData<HiZMaxDepthData>();

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                pass.SetFloat("_MaxHiZMip", Texture2DExtensions.MipCount(viewSize) - 1);
                pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                pass.SetInt("_InstanceCount", instanceData.instanceCount);
            });
        }

        var prefixSums = renderGraph.GetBuffer(instanceData.instanceCount);
        instancePrefixSum.GetThreadGroupSizes(0, instanceData.instanceCount, out var groupsX);
        var groupSums = renderGraph.GetBuffer((int)groupsX);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Prefix Sum 1"))
        {
            pass.Initialize(instancePrefixSum, 0, instanceData.instanceCount);
            pass.WriteBuffer("PrefixSumsWrite", prefixSums);
            pass.WriteBuffer("GroupSumsWrite", groupSums);

            pass.ReadBuffer("Input", visibilityPredicates);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", instanceData.instanceCount);
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
		var instanceIndices = renderGraph.GetBuffer(instanceData.instanceCount);
		var lodCounts = renderGraph.GetBuffer(instanceData.lodCount);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Stream Compaction"))
        {
            pass.Initialize(instanceCompaction, 0, instanceData.instanceCount);
            pass.WriteBuffer("Output", instanceIndices);
			pass.WriteBuffer("LodCounts", lodCounts);

			pass.ReadBuffer("Input", visibilityPredicates);
            pass.ReadBuffer("PrefixSums", prefixSums);
            pass.ReadBuffer("GroupSums", groupSums1);
            pass.ReadBuffer("InstanceBounds", instanceData.instanceBounds);
			pass.ReadBuffer("InstanceTypeIds", instanceData.instanceTypes);
			pass.ReadBuffer("InstanceTypeDatas", instanceData.instanceTypeData);

			pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", instanceData.instanceCount);

				// For lod maybe?
                pass.SetVector("CameraForward", (Vector3)mainViewForward);
                pass.SetVector("ViewPosition", (Vector3)mainViewPosition);

				using (ArrayPool<uint>.Get(instanceData.lodCount, out var data))
					command.SetBufferData(pass.GetBuffer(lodCounts), data);
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

			pass.SetRenderFunction((command, pass) =>
			{
				var b = pass.GetBuffer(instanceData.rendererLodIndices);

				pass.SetInt("MaxThread", instanceData.rendererCount);
			});
		}

		// Write out the offset for each type
		var instanceIdOffsetsBuffer = renderGraph.GetBuffer(instanceData.lodCount);
		using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Id Offsets"))
		{
			pass.Initialize(instanceIdOffsets, 0, 1, 1, 1, false);
			pass.WriteBuffer("Output", instanceIdOffsetsBuffer);
			pass.ReadBuffer("LodCounts", lodCounts);

			pass.SetRenderFunction((command, pass) =>
			{
				pass.SetInt("MaxThread", instanceData.lodCount);
			});
		}

		var threadGroups = renderGraph.GetBuffer(6, target: GraphicsBuffer.Target.IndirectArguments);
        using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("ComputeThreadCount"))
        {
            pass.Initialize(instanceCopyData, 0, 1, 1, 1, false);
            pass.WriteBuffer("ThreadGroupsWrite", threadGroups);
            pass.ReadBuffer("TotalInstanceCount", totalInstanceCountBuffer);
        }

        var objectToWorld = renderGraph.GetBuffer(instanceData.instanceCount, UnsafeUtility.SizeOf<Float3x4>());
        using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Copy Data"))
        {
            pass.Initialize(instanceCopyData, threadGroups, 1);
            pass.WriteBuffer("_ObjectToWorldWrite", objectToWorld);

            pass.ReadBuffer("InputIndices", instanceIndices);
            pass.ReadBuffer("_Positions", instanceData.positions);
            pass.ReadBuffer("TotalInstanceCount", totalInstanceCountBuffer);

            pass.SetRenderFunction((command, pass) =>
            {
                pass.SetInt("MaxThread", instanceData.instanceCount);
            });
        }

		return new GpuRenderingData(visibilityPredicates, objectToWorld, instanceIdOffsetsBuffer);
    }

	public void RenderShadow(Float3 mainViewPosition, Float3 mainViewForward, ShadowRequestData request, Int2 viewSize)
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

		var renderingData = Render(viewSize, mainViewPosition, mainViewForward, true, cullingPlanes, instanceData);

		for (var i = 0; i < drawList.Count; i++)
		{
			var draw = drawList[i];
			using (var pass = renderGraph.AddRenderPass<DrawInstancedIndirectRenderPass>("Gpu Driven Rendering Shadow"))
			{
				pass.Initialize(draw.mesh, draw.submeshIndex, draw.material, instanceData.drawCallArgs, draw.passIndex, "INDIRECT_RENDERING", request.Bias, request.SlopeBias, request.ZClip, draw.indirectArgsOffset);

				pass.WriteTexture(request.Shadow);
				pass.DepthSlice = request.CascadeIndex;

				pass.AddRenderPassData<ViewData>();
				pass.AddRenderPassData<FrameData>();

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

public class GpuDrivenRenderingRender : CameraRenderFeature
{
	private GpuDrivenRenderer gpuDrivenRenderer;

	public GpuDrivenRenderingRender(GpuDrivenRenderer gpuDrivenRenderer, RenderGraph renderGraph) : base(renderGraph)
	{
		this.gpuDrivenRenderer = gpuDrivenRenderer;
	}

	public override void Render(Camera camera, ScriptableRenderContext context)
	{
		if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game && camera.cameraType != CameraType.Reflection)
			return;

		var handle = renderGraph.ResourceMap.GetResourceHandle<GpuDrivenRenderingData>();
		if (!renderGraph.ResourceMap.TryGetRenderPassData<GpuDrivenRenderingData>(handle, renderGraph.FrameIndex, out var instanceData))
			return;

		if (!instanceData.rendererDrawCallData.TryGetValue("MotionVectors", out var drawList))
			return;

		var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().CullingPlanes;
		var renderingData = gpuDrivenRenderer.Render(camera.ScaledViewSize(), camera.transform.position, camera.transform.forward, false, cullingPlanes, instanceData);

		for (var i = 0; i < drawList.Count; i++)
		{
			var draw = drawList[i];
			using (var pass = renderGraph.AddRenderPass<DrawInstancedIndirectRenderPass>("Gpu Driven Rendering"))
			{
				pass.Initialize(draw.mesh, draw.submeshIndex, draw.material, instanceData.drawCallArgs, draw.passIndex, "INDIRECT_RENDERING", 0.0f, 0.0f, true, draw.indirectArgsOffset);

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