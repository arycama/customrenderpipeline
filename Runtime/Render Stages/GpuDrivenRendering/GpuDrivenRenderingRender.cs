using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class GpuDrivenRenderingRender : RenderFeature
    {
        private static readonly uint[] emptyCounter = new uint[1];

        private readonly ComputeShader clearShader, cullingShader, scanShader, compactShader;

        public GpuDrivenRenderingRender(RenderGraph renderGraph) : base(renderGraph)
        {
            clearShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceClear");
            cullingShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceRendererCull");
            scanShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceScan");
            compactShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCompaction");

        }

        public override void Render()
        {
            var camera = renderGraph.GetResource<ViewData>().Camera;

            if (camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game && camera.cameraType != CameraType.Reflection)
                return;

            var handle = renderGraph.ResourceMap.GetResourceHandle<GpuInstanceBuffersData>();
            if (!renderGraph.ResourceMap.TryGetRenderPassData<GpuInstanceBuffersData>(handle, renderGraph.FrameIndex, out var gpuInstanceBuffers))
                return;

            var viewData = renderGraph.GetResource<ViewResolutionData>();
            var hiZTexture = renderGraph.GetResource<HiZMinDepthData>().Handle;
            var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().CullingPlanes;

            var viewMatrix = camera.worldToCameraMatrix;
            viewMatrix.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

            var screenMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * viewMatrix;

            var rendererInstanceIDsBuffer = renderGraph.GetBuffer(gpuInstanceBuffers.instanceTimesRendererCount);
            var visibleRendererInstanceIndicesBuffer = renderGraph.GetBuffer(gpuInstanceBuffers.instanceTimesRendererCount);
            var rendererCountsBuffer = renderGraph.GetBuffer(gpuInstanceBuffers.totalRendererSum);
            var rendererInstanceIndexOffsetsBuffer = renderGraph.GetBuffer(gpuInstanceBuffers.totalRendererSum);
            var finalRendererCountsBuffer = renderGraph.GetBuffer(gpuInstanceBuffers.totalRendererSum);
            var memoryCounterBuffer = renderGraph.GetBuffer();

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Gpu Instance Clear"))
            {
                pass.WriteBuffer("", rendererInstanceIDsBuffer);
                pass.WriteBuffer("", rendererInstanceIndexOffsetsBuffer);
                pass.WriteBuffer("", rendererCountsBuffer);
                pass.WriteBuffer("", finalRendererCountsBuffer);
                pass.WriteBuffer("", visibleRendererInstanceIndicesBuffer);
                pass.WriteBuffer("", memoryCounterBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(rendererInstanceIDsBuffer));
                    command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.instanceTimesRendererCount, 1, 1);

                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(rendererInstanceIndexOffsetsBuffer));
                    command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.totalRendererSum, 1, 1);

                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(rendererCountsBuffer));
                    command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.totalRendererSum, 1, 1);

                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(finalRendererCountsBuffer));
                    command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.totalRendererSum, 1, 1);

                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(visibleRendererInstanceIndicesBuffer));
                    command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.instanceTimesRendererCount, 1, 1);

                    command.SetBufferData(renderGraph.BufferHandleSystem.GetResource(memoryCounterBuffer), emptyCounter);
                });
            }

            var isShadow = false; // TODO: Support?
            var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
            for (var i = 0; i < cullingPlanes.Count; i++)
                cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Cull"))
            {
                pass.Initialize(cullingShader, 0, gpuInstanceBuffers.totalInstanceCount);

                if (!isShadow)
                    pass.AddKeyword("HIZ_ON");

                pass.WriteBuffer("_RendererInstanceIDs", rendererInstanceIDsBuffer);
                pass.WriteBuffer("_RendererCounts", rendererCountsBuffer);
                pass.WriteBuffer("_LodFades", gpuInstanceBuffers.lodFadesBuffer);

                pass.ReadBuffer("_Positions", gpuInstanceBuffers.positionsBuffer);
                pass.ReadBuffer("_InstanceTypes", gpuInstanceBuffers.instanceTypeIdsBuffer);
                pass.ReadBuffer("_RendererBounds", gpuInstanceBuffers.rendererBoundsBuffer);
                pass.ReadBuffer("_LodSizes", gpuInstanceBuffers.lodSizesBuffer);
                pass.ReadBuffer("_InstanceTypeData", gpuInstanceBuffers.instanceTypeDataBuffer);
                pass.ReadBuffer("_InstanceTypeLodData", gpuInstanceBuffers.instanceTypeLodDataBuffer);

                pass.ReadTexture("_CameraMaxZTexture", hiZTexture);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetMatrix("_ScreenMatrix", screenMatrix);
                    pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                    pass.SetInt("_MaxHiZMip", Texture2DExtensions.MipCount(viewData.ScaledWidth, viewData.ScaledHeight) - 1);
                    pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                    pass.SetInt("_InstanceCount", gpuInstanceBuffers.totalInstanceCount);
                });
            }

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Scan"))
            {
                pass.Initialize(scanShader, 0, gpuInstanceBuffers.totalRendererSum);

                pass.WriteBuffer("_RendererInstanceIndexOffsets", rendererInstanceIndexOffsetsBuffer);
                pass.WriteBuffer("_DrawCallArgs", gpuInstanceBuffers.drawCallArgsBuffer);
                pass.WriteBuffer("_MemoryCounter", memoryCounterBuffer);

                pass.ReadBuffer("_MemoryCounter", memoryCounterBuffer);
                pass.ReadBuffer("_RendererCounts", rendererCountsBuffer);
                pass.ReadBuffer("_SubmeshOffsetLengths", gpuInstanceBuffers.submeshOffsetLengthsBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("_Count", gpuInstanceBuffers.totalRendererSum);
                });
            }

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Compact"))
            {
                pass.Initialize(compactShader, 0, gpuInstanceBuffers.totalInstanceCount);

                pass.WriteBuffer("_FinalRendererCounts", finalRendererCountsBuffer);
                pass.WriteBuffer("_VisibleRendererInstanceIndices", visibleRendererInstanceIndicesBuffer);

                pass.ReadBuffer("_RendererInstanceIDs", rendererInstanceIDsBuffer);
                pass.ReadBuffer("_RendererInstanceIndexOffsets", rendererInstanceIndexOffsetsBuffer);
                pass.ReadBuffer("_InstanceTypeIds", gpuInstanceBuffers.instanceTypeIdsBuffer);
                pass.ReadBuffer("_InstanceTypeData", gpuInstanceBuffers.instanceTypeDataBuffer);
                pass.ReadBuffer("_InstanceTypeLodData", gpuInstanceBuffers.instanceTypeLodDataBuffer);
                pass.ReadBuffer("_FinalRendererCounts", finalRendererCountsBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("_RendererInstanceIDsCount", gpuInstanceBuffers.totalInstanceCount);
                });
            }

            var passName = "MotionVectors";
            if (!gpuInstanceBuffers.rendererDrawCallData.TryGetValue(passName, out var drawList))
                return;

            // Render instances
            foreach (var draw in drawList)
            {
                var renderQueueMin = 0;
                var renderQueueMax = 2500;

                if (draw.renderQueue < renderQueueMin || draw.renderQueue > renderQueueMax)
                    continue;

                using (var pass = renderGraph.AddRenderPass<DrawInstancedIndirectRenderPass>("Gpu Driven Rendering"))
                {
                    pass.Initialize(draw.mesh, draw.submeshIndex, draw.material, gpuInstanceBuffers.drawCallArgsBuffer, draw.passIndex, "INDIRECT_RENDERING", 0.0f, 0.0f, true, draw.indirectArgsOffset);

                    pass.WriteDepth(renderGraph.GetResource<CameraDepthData>());
                    pass.WriteTexture(renderGraph.GetResource<AlbedoMetallicData>());
                    pass.WriteTexture(renderGraph.GetResource<NormalRoughnessData>());
                    pass.WriteTexture(renderGraph.GetResource<BentNormalOcclusionData>());
                    pass.WriteTexture(renderGraph.GetResource<CameraTargetData>());
                    pass.WriteTexture(renderGraph.GetResource<VelocityData>());
                    pass.AddRenderPassData<AutoExposureData>();
                    pass.AddRenderPassData<TemporalAAData>();
                    pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                    pass.AddRenderPassData<ICommonPassData>();

                    pass.ReadBuffer("_RendererInstanceIndexOffsets", rendererInstanceIndexOffsetsBuffer);
                    pass.ReadBuffer("_VisibleRendererInstanceIndices", visibleRendererInstanceIndicesBuffer);
                    pass.ReadBuffer("_InstancePositions", gpuInstanceBuffers.positionsBuffer);
                    pass.ReadBuffer("_InstanceLodFades", gpuInstanceBuffers.lodFadesBuffer);

                    pass.SetRenderFunction((draw.rendererOffset, draw.localToWorld), (command, pass, data) =>
                    {
                        pass.SetInt("RendererOffset", data.rendererOffset);
                        pass.SetVector("unity_WorldTransformParams", Vector4.one);
                        pass.SetMatrix("_LocalToWorld", data.localToWorld);
                    });
                }
            }
        }
    }
}