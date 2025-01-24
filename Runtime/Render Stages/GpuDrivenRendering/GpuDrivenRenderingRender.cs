using Unity.Collections.LowLevel.Unsafe;
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
            var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().CullingPlanes;

            var viewMatrix = camera.worldToCameraMatrix;
            viewMatrix.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

            var screenMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * viewMatrix;

            var isShadow = false; // TODO: Support?
            var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
            for (var i = 0; i < cullingPlanes.Count; i++)
                cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

            var rendererInstanceIDsBuffer = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Cull"))
            {
                pass.Initialize(cullingShader, 0, gpuInstanceBuffers.totalInstanceCount);

                if (!isShadow)
                    pass.AddKeyword("HIZ_ON");

                pass.WriteBuffer("_RendererInstanceIDs", rendererInstanceIDsBuffer);
                pass.ReadBuffer("_InstanceBounds", gpuInstanceBuffers.instanceBoundsBuffer);

                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<HiZMaxDepthData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetMatrix("_ScreenMatrix", screenMatrix);
                    pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                    pass.SetFloat("_MaxHiZMip", Texture2DExtensions.MipCount(viewData.ScaledWidth, viewData.ScaledHeight) - 1);
                    pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                    pass.SetInt("_InstanceCount", gpuInstanceBuffers.totalInstanceCount);
                });
            }

            var objectToWorld = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount, UnsafeUtility.SizeOf<Matrix3x4>(), target: GraphicsBuffer.Target.Counter);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Scan"))
            {
                pass.Initialize(scanShader, 0, gpuInstanceBuffers.totalInstanceCount);
                pass.WriteBuffer("_ObjectToWorldWrite", objectToWorld);
                pass.ReadBuffer("_RendererInstanceIDs", rendererInstanceIDsBuffer);
                pass.ReadBuffer("_Positions", gpuInstanceBuffers.positionsBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    command.SetBufferCounterValue(pass.GetBuffer(objectToWorld), 0);
                    pass.SetInt("_InstanceCount", gpuInstanceBuffers.totalInstanceCount);
                });
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Instance Rendering Copy Count"))
            {
                pass.SetRenderFunction((command, pass) =>
                {
                    command.CopyCounterValue(pass.GetBuffer(objectToWorld), pass.GetBuffer(gpuInstanceBuffers.drawCallArgsBuffer), 4); 
                });
            }

            if (!gpuInstanceBuffers.rendererDrawCallData.TryGetValue("MotionVectors", out var drawList))
                return;

            // Render instances
            foreach (var draw in drawList)
            {
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

                    pass.ReadBuffer("_VisibleRendererInstanceIndices", rendererInstanceIDsBuffer);
                    pass.ReadBuffer("_ObjectToWorld", objectToWorld);
                    pass.ReadBuffer("_InstancePositions", gpuInstanceBuffers.positionsBuffer);
                    pass.ReadBuffer("_InstanceLodFades", gpuInstanceBuffers.lodFadesBuffer);
                }
            }
        }
    }
}