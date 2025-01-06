using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class GpuDrivenRenderingRender : RenderFeature
    {
        private static readonly uint[] emptyCounter = new uint[1];

        private readonly ComputeShader clearShader, cullingShader, scanShader, compactShader;
        private ComputeBuffer memoryCounterBuffer;
        private MaterialPropertyBlock propertyBlock;

        public GpuDrivenRenderingRender(RenderGraph renderGraph) : base(renderGraph)
        {
            clearShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceClear");
            cullingShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceRendererCull");
            scanShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceScan");
            compactShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCompaction");

            memoryCounterBuffer = new ComputeBuffer(1, sizeof(uint));
            propertyBlock = new();
        }

        protected override void Cleanup(bool disposing)
        {
            GraphicsUtilities.SafeDestroy(ref memoryCounterBuffer);
        }

        public override void Render()
        {
            var camera = renderGraph.GetResource<ViewData>().Camera;
            var handle = renderGraph.ResourceMap.GetResourceHandle<GpuInstanceBuffersData>();
            if (!renderGraph.ResourceMap.TryGetRenderPassData<GpuInstanceBuffersData>(handle, renderGraph.FrameIndex, out var gpuInstanceBuffersData))
                return;

            var gpuInstanceBuffers = gpuInstanceBuffersData.Data;
            if ((camera.cameraType != CameraType.SceneView && camera.cameraType != CameraType.Game && camera.cameraType != CameraType.Reflection) || gpuInstanceBuffers.rendererInstanceIDsBuffer == null)
                return;

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Gpu Instance Culling"))
            {
                var viewData = renderGraph.GetResource<ViewResolutionData>();
                var hiZTexture = renderGraph.GetResource<HiZMinDepthData>().Handle;
                var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().CullingPlanes;

                pass.SetRenderFunction((command, pass) =>
                {
                    var viewMatrix = camera.worldToCameraMatrix;
                    viewMatrix.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

                    var screenMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * viewMatrix;

                    //using (var profilerScope = command.ProfilerScope("Clear"))
                    {
                        command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.rendererInstanceIDsBuffer);
                        command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.rendererInstanceIDsBuffer.count, 1, 1);
                        command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
                        command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer.count, 1, 1);
                        command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.rendererCountsBuffer);
                        command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.rendererCountsBuffer.count, 1, 1);
                        command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.finalRendererCountsBuffer);
                        command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.finalRendererCountsBuffer.count, 1, 1);
                        command.SetComputeBufferParam(clearShader, 0, "_Result", gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer);
                        command.DispatchNormalized(clearShader, 0, gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer.count, 1, 1);
                    }

                    // Culling shader
                    //using (var profilerScope = command.ProfilerScope("Instance Cull"))
                    var isShadow = false; // TODO: Support?
                   
                    var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                    for (var i = 0; i < cullingPlanes.Count; i++)
                        cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                    {
                        command.SetBufferData(memoryCounterBuffer, emptyCounter);

                        command.SetComputeBufferParam(cullingShader, 0, "_Positions", gpuInstanceBuffers.positionsBuffer);
                        command.SetComputeBufferParam(cullingShader, 0, "_InstanceTypes", gpuInstanceBuffers.instanceTypeIdsBuffer);
                        command.SetComputeBufferParam(cullingShader, 0, "_LodFades", gpuInstanceBuffers.lodFadesBuffer);
                        command.SetComputeBufferParam(cullingShader, 0, "_RendererBounds", gpuInstanceBuffers.rendererBoundsBuffer);
                        command.SetComputeBufferParam(cullingShader, 0, "_RendererInstanceIDs", gpuInstanceBuffers.rendererInstanceIDsBuffer);
                        command.SetComputeBufferParam(cullingShader, 0, "_RendererCounts", gpuInstanceBuffers.rendererCountsBuffer);
                        command.SetComputeBufferParam(cullingShader, 0, "_LodSizes", gpuInstanceBuffers.lodSizesBuffer);
                        command.SetComputeBufferParam(cullingShader, 0, "_InstanceTypeData", gpuInstanceBuffers.instanceTypeDataBuffer);
                        command.SetComputeBufferParam(cullingShader, 0, "_InstanceTypeLodData", gpuInstanceBuffers.instanceTypeLodDataBuffer);

                        command.SetComputeTextureParam(cullingShader, 0, "_CameraMaxZTexture", pass.GetRenderTexture(hiZTexture));

                        command.SetComputeMatrixParam(cullingShader, "_ScreenMatrix", screenMatrix);
                        command.SetComputeVectorArrayParam(cullingShader, "_CullingPlanes", cullingPlanesArray);
                        command.SetComputeVectorParam(cullingShader, "_Resolution", new Vector4(1f / viewData.ScaledWidth, 1f / viewData.ScaledHeight, viewData.ScaledWidth, viewData.ScaledHeight));
                        command.SetComputeIntParam(cullingShader, "_MaxHiZMip", Texture2DExtensions.MipCount(viewData.ScaledWidth, viewData.ScaledHeight) - 1);
                        command.SetComputeIntParam(cullingShader, "_CullingPlanesCount", cullingPlanes.Count);
                        command.SetComputeIntParam(cullingShader, "_InstanceCount", gpuInstanceBuffers.positionsBuffer.count);

                        using var hiZScope = command.KeywordScope("HIZ_ON", !isShadow);
                        command.DispatchNormalized(cullingShader, 0, gpuInstanceBuffers.positionsBuffer.count, 1, 1);
                    }

                    //using (var profilerScope = command.ProfilerScope("Instance Scan"))
                    {
                        command.SetComputeIntParam(scanShader, "_Count", gpuInstanceBuffers.rendererCountsBuffer.count);
                        command.SetComputeBufferParam(scanShader, 0, "_MemoryCounter", memoryCounterBuffer);
                        command.SetComputeBufferParam(scanShader, 0, "_RendererCounts", gpuInstanceBuffers.rendererCountsBuffer);
                        command.SetComputeBufferParam(scanShader, 0, "_SubmeshOffsetLengths", gpuInstanceBuffers.submeshOffsetLengthsBuffer);
                        command.SetComputeBufferParam(scanShader, 0, "_RendererInstanceIndexOffsets", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
                        command.SetComputeBufferParam(scanShader, 0, "_DrawCallArgs", gpuInstanceBuffers.drawCallArgsBuffer);
                        command.DispatchNormalized(scanShader, 0, gpuInstanceBuffers.rendererCountsBuffer.count, 1, 1);
                    }

                    //using (var profilerScope = command.ProfilerScope("Instance Compact"))
                    {
                        command.SetComputeIntParam(compactShader, "_RendererInstanceIDsCount", gpuInstanceBuffers.positionsBuffer.count);
                        command.SetComputeBufferParam(compactShader, 0, "_RendererInstanceIDs", gpuInstanceBuffers.rendererInstanceIDsBuffer);
                        command.SetComputeBufferParam(compactShader, 0, "_RendererInstanceIndexOffsets", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
                        command.SetComputeBufferParam(compactShader, 0, "_FinalRendererCounts", gpuInstanceBuffers.finalRendererCountsBuffer);
                        command.SetComputeBufferParam(compactShader, 0, "_VisibleRendererInstanceIndices", gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer);
                        command.SetComputeBufferParam(compactShader, 0, "_InstanceTypeIds", gpuInstanceBuffers.instanceTypeIdsBuffer);
                        command.SetComputeBufferParam(compactShader, 0, "_InstanceTypeData", gpuInstanceBuffers.instanceTypeDataBuffer);
                        command.SetComputeBufferParam(compactShader, 0, "_InstanceTypeLodData", gpuInstanceBuffers.instanceTypeLodDataBuffer);
                        command.DispatchNormalized(compactShader, 0, gpuInstanceBuffers.positionsBuffer.count, 1, 1);
                    }
                });
            }

            if (gpuInstanceBuffers.readyInstanceData == null)
                return;

            var passName = "MotionVectors";
            if (gpuInstanceBuffers.rendererDrawCallData.TryGetValue(passName, out var drawList))
            {
                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Gpu Driven Rendering"))
                {
                    var depth = renderGraph.GetResource<CameraDepthData>().Handle;
                    var albedoMetallic = renderGraph.GetResource<AlbedoMetallicData>().Handle;
                    var normalRoughness = renderGraph.GetResource<NormalRoughnessData>().Handle;
                    var bentNormalOcclusion = renderGraph.GetResource<BentNormalOcclusionData>().Handle;
                    var cameraTarget = renderGraph.GetResource<CameraTargetData>().Handle;
                    var velocity = renderGraph.GetResource<VelocityData>().Handle;

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

                    // Render instances


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

                        command.SetRenderTarget(rtis, pass.GetRenderTexture(depth));
                        command.EnableShaderKeyword("INDIRECT_RENDERING");

                        var ind = 0;
                        foreach (var draw in drawList)
                        {
                            var renderQueueMin = 0;
                            var renderQueueMax = 2500;

                            if (draw.renderQueue < renderQueueMin || draw.renderQueue > renderQueueMax)
                                continue;

                            propertyBlock.Clear();
                            propertyBlock.SetBuffer("_RendererInstanceIndexOffsets", gpuInstanceBuffers.rendererInstanceIndexOffsetsBuffer);
                            propertyBlock.SetBuffer("_VisibleRendererInstanceIndices", gpuInstanceBuffers.visibleRendererInstanceIndicesBuffer);

                            propertyBlock.SetBuffer("_InstancePositions", gpuInstanceBuffers.positionsBuffer);
                            propertyBlock.SetBuffer("_InstanceLodFades", gpuInstanceBuffers.lodFadesBuffer);

                            propertyBlock.SetInt("RendererOffset", draw.rendererOffset);
                            propertyBlock.SetVector("unity_WorldTransformParams", Vector4.one);
                            propertyBlock.SetMatrix("_LocalToWorld", draw.localToWorld);
                            //command.DrawMeshInstancedIndirect(draw.mesh, draw.submeshIndex, draw.material, draw.passIndex, gpuInstanceBuffers.drawCallArgsBuffer, draw.indirectArgsOffset, propertyBlock);
                        }
                    });
                }

                using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Gpu Driven Rendering"))
                {
                    pass.SetRenderFunction((command, pass) =>
                    {
                        command.DisableShaderKeyword("INDIRECT_RENDERING");
                    });
                }
            }
        }
    }
}