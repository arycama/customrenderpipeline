using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class GpuDrivenRenderingRender : RenderFeature
    {
        private static readonly uint[] emptyCounter = new uint[1];

        private readonly ComputeShader clearShader, cullingShader, scanShader, compactShader;
        private ResourceHandle<GraphicsBuffer> memoryCounterBuffer;

        public GpuDrivenRenderingRender(RenderGraph renderGraph) : base(renderGraph)
        {
            clearShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceClear");
            cullingShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceRendererCull");
            scanShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceScan");
            compactShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCompaction");

            memoryCounterBuffer = renderGraph.GetBuffer(isPersistent: true);
        }

        protected override void Cleanup(bool disposing)
        {
            renderGraph.ReleasePersistentResource(memoryCounterBuffer);
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

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Gpu Instance Clear"))
            {
                pass.WriteBuffer("", gpuInstanceBuffers.rendererInstanceIDsBuffer);
                pass.WriteBuffer("", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
                pass.WriteBuffer("", gpuInstanceBuffers.rendererCountsBuffer);
                pass.WriteBuffer("", gpuInstanceBuffers.finalRendererCountsBuffer);
                pass.WriteBuffer("", gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer);
                pass.WriteBuffer("", memoryCounterBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(gpuInstanceBuffers.rendererInstanceIDsBuffer));
                    command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.instanceTimesRendererCount, 1, 1);

                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer));
                    command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.totalRendererSum, 1, 1);

                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(gpuInstanceBuffers.rendererCountsBuffer));
                    command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.totalRendererSum, 1, 1);

                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(gpuInstanceBuffers.finalRendererCountsBuffer));
                    command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.totalRendererSum, 1, 1);

                    command.SetComputeBufferParam(clearShader, 0, "_Result", pass.GetBuffer(gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer));
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

                pass.WriteBuffer("_RendererInstanceIDs", gpuInstanceBuffers.rendererInstanceIDsBuffer);
                pass.WriteBuffer("_RendererCounts", gpuInstanceBuffers.rendererCountsBuffer);
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

                pass.WriteBuffer("_RendererCounts", gpuInstanceBuffers.rendererCountsBuffer);
                pass.WriteBuffer("_RendererInstanceIndexOffsets", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
                pass.WriteBuffer("_DrawCallArgs", gpuInstanceBuffers.drawCallArgsBuffer);
                pass.WriteBuffer("_MemoryCounter", memoryCounterBuffer);

                pass.ReadBuffer("_SubmeshOffsetLengths", gpuInstanceBuffers.submeshOffsetLengthsBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("_Count", gpuInstanceBuffers.totalRendererSum);
                });
            }

            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Compact"))
            {
                pass.Initialize(compactShader, 0, gpuInstanceBuffers.totalInstanceCount);

                pass.WriteBuffer("_FinalRendererCounts", gpuInstanceBuffers.finalRendererCountsBuffer);
                pass.WriteBuffer("_VisibleRendererInstanceIndices", gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer);

                pass.ReadBuffer("_RendererInstanceIDs", gpuInstanceBuffers.rendererInstanceIDsBuffer);
                pass.ReadBuffer("_RendererInstanceIndexOffsets", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
                pass.ReadBuffer("_InstanceTypeIds", gpuInstanceBuffers.instanceTypeIdsBuffer);
                pass.ReadBuffer("_InstanceTypeData", gpuInstanceBuffers.instanceTypeDataBuffer);
                pass.ReadBuffer("_InstanceTypeLodData", gpuInstanceBuffers.instanceTypeLodDataBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("_RendererInstanceIDsCount", gpuInstanceBuffers.totalInstanceCount);
                });
            }

            var passName = "MotionVectors";
            if (!gpuInstanceBuffers.rendererDrawCallData.TryGetValue(passName, out var drawList))
                return;

            var depth = renderGraph.GetResource<CameraDepthData>().Handle;
            var albedoMetallic = renderGraph.GetResource<AlbedoMetallicData>().Handle;
            var normalRoughness = renderGraph.GetResource<NormalRoughnessData>().Handle;
            var bentNormalOcclusion = renderGraph.GetResource<BentNormalOcclusionData>().Handle;
            var cameraTarget = renderGraph.GetResource<CameraTargetData>().Handle;
            var velocity = renderGraph.GetResource<VelocityData>().Handle;

            // Render instances
            foreach (var draw in drawList)
            {
                var renderQueueMin = 0;
                var renderQueueMax = 2500;

                if (draw.renderQueue < renderQueueMin || draw.renderQueue > renderQueueMax)
                    continue;

                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Gpu Driven Rendering"))
                {
                    pass.WriteTexture(depth);
                    pass.WriteTexture(albedoMetallic);
                    pass.WriteTexture(normalRoughness);
                    pass.WriteTexture(bentNormalOcclusion);
                    pass.WriteTexture(cameraTarget);
                    pass.WriteTexture(velocity);
                    pass.AddRenderPassData<AutoExposureData>();
                    pass.AddRenderPassData<TemporalAAData>();
                    pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                    pass.AddRenderPassData<ICommonPassData>();

                    pass.ReadBuffer("_RendererInstanceIndexOffsets", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
                    pass.ReadBuffer("_VisibleRendererInstanceIndices", gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer);
                    pass.ReadBuffer("_InstancePositions", gpuInstanceBuffers.positionsBuffer);
                    pass.ReadBuffer("_InstanceLodFades", gpuInstanceBuffers.lodFadesBuffer);

                    pass.SetRenderFunction((command, pass) =>
                    {
                        var rtis = new RenderTargetIdentifier[5]
                        {
                            pass.GetRenderTexture(albedoMetallic),
                            pass.GetRenderTexture(normalRoughness),
                            pass.GetRenderTexture(bentNormalOcclusion),
                            pass.GetRenderTexture(cameraTarget),
                            pass.GetRenderTexture(velocity)
                        };

                        pass.SetInt("RendererOffset", draw.rendererOffset);
                        pass.SetVector("unity_WorldTransformParams", Vector4.one);
                        pass.SetMatrix("_LocalToWorld", draw.localToWorld);

                        command.SetRenderTarget(rtis, pass.GetRenderTexture(depth));
                        command.EnableShaderKeyword("INDIRECT_RENDERING");
                        command.DrawMeshInstancedIndirect(draw.mesh, draw.submeshIndex, draw.material, draw.passIndex, renderGraph.BufferHandleSystem.GetResource(gpuInstanceBuffers.drawCallArgsBuffer), draw.indirectArgsOffset);
                        command.DisableShaderKeyword("INDIRECT_RENDERING");
                    });
                }
            }
        }
    }
}