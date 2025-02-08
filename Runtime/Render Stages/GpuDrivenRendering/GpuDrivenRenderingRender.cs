using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class GpuDrivenRenderingRender : RenderFeature
    {
        private readonly ComputeShader cullingShader, instancePrefixSum, instanceSort, instanceCompaction, instanceCopyData;

        public GpuDrivenRenderingRender(RenderGraph renderGraph) : base(renderGraph)
        {
            cullingShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceRendererCull");
            instancePrefixSum = Resources.Load<ComputeShader>("GpuInstancedRendering/InstancePrefixSum");
            instanceSort = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceSort");
            instanceCompaction = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCompaction");
            instanceCopyData = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceCopyData");
        }

        public override void Render()
        {
            var viewData = renderGraph.GetResource<ViewData>();
            var camera = viewData.Camera;

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

                pass.AddRenderPassData<ICommonPassData>();
                pass.AddRenderPassData<HiZMaxDepthData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                    pass.SetFloat("_MaxHiZMip", Texture2DExtensions.MipCount(viewData.ScaledWidth, viewData.ScaledHeight) - 1);
                    pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                    pass.SetInt("_InstanceCount", gpuInstanceBuffers.totalInstanceCount);
                });
            }

            var prefixSums = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
            instancePrefixSum.GetThreadGroupSizes(0, gpuInstanceBuffers.totalInstanceCount / 2, out var groupsX);
            var groupSums = renderGraph.GetBuffer((int)groupsX);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Prefix Sum 1"))
            {
                pass.Initialize(instancePrefixSum, 0, gpuInstanceBuffers.totalInstanceCount / 2);
                pass.WriteBuffer("PrefixSumsWrite", prefixSums);
                pass.WriteBuffer("GroupSumsWrite", groupSums);

                pass.ReadBuffer("Input", visibilityPredicates);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("MaxThread", gpuInstanceBuffers.totalInstanceCount / 2);
                });
            }

            // TODO: This only handles a 2048*1024 array for now
            var groupSums1 = renderGraph.GetBuffer((int)groupsX);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Prefix Sum 2"))
            {
                pass.Initialize(instancePrefixSum, 1, (int)groupsX / 2);
                pass.WriteBuffer("PrefixSumsWrite", groupSums1);
                pass.WriteBuffer("DrawCallArgsWrite", gpuInstanceBuffers.drawCallArgsBuffer);
                pass.ReadBuffer("Input", groupSums);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("MaxThread", (int)groupsX / 2);
                });
            }

            // Need a buffer big enough to hold all potential indices
            var instanceIndices = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
            var sortKeys = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Stream Compaction"))
            {
                pass.Initialize(instanceCompaction, 0, gpuInstanceBuffers.totalInstanceCount);
                pass.WriteBuffer("Output", instanceIndices);
                pass.WriteBuffer("SortKeysWrite", sortKeys);

                pass.ReadBuffer("Input", visibilityPredicates);
                pass.ReadBuffer("PrefixSums", prefixSums);
                pass.ReadBuffer("GroupSums", groupSums1);
                pass.ReadBuffer("InstanceBounds", gpuInstanceBuffers.instanceBoundsBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("MaxThread", gpuInstanceBuffers.totalInstanceCount);
                    pass.SetVector("CameraForward", viewData.Camera.transform.forward);
                    pass.SetVector("ViewPosition", viewData.Camera.transform.position);
                });
            }

            var threadGroups = renderGraph.GetBuffer(3, target: GraphicsBuffer.Target.IndirectArguments);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("ComputeThreadCount"))
            {
                pass.Initialize(instanceCopyData, 0, 1, 1, 1, false);
                pass.WriteBuffer("ThreadGroupsWrite", threadGroups);
                pass.ReadBuffer("DrawCallArgs", gpuInstanceBuffers.drawCallArgsBuffer);
            }

            var sortedInstanceIndices = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
            var sortedKeys = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
            using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Instance Sort"))
            {
                pass.Initialize(instanceSort, threadGroups);
                pass.WriteBuffer("Result", sortedInstanceIndices);
                pass.WriteBuffer("SortKeysWrite", sortedKeys);

                pass.ReadBuffer("Input", instanceIndices);
                pass.ReadBuffer("SortKeys", sortKeys);
                pass.ReadBuffer("DrawCallArgs", gpuInstanceBuffers.drawCallArgsBuffer);
            }

            var objectToWorld = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount, UnsafeUtility.SizeOf<Matrix3x4>());
            using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Copy Data"))
            {
                pass.Initialize(instanceCopyData, threadGroups, 1);
                pass.WriteBuffer("_ObjectToWorldWrite", objectToWorld);

                pass.ReadBuffer("InputIndices", sortedInstanceIndices);
                pass.ReadBuffer("_Positions", gpuInstanceBuffers.positionsBuffer);
                pass.ReadBuffer("DrawCallArgs", gpuInstanceBuffers.drawCallArgsBuffer);

                // Just here for debug
                pass.ReadBuffer("SortedKeys", sortedKeys);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("MaxThread", gpuInstanceBuffers.totalInstanceCount);
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

                    pass.ReadBuffer("_VisibleRendererInstanceIndices", visibilityPredicates);
                    pass.ReadBuffer("_ObjectToWorld", objectToWorld);
                    pass.ReadBuffer("_InstancePositions", gpuInstanceBuffers.positionsBuffer);
                    pass.ReadBuffer("_InstanceLodFades", gpuInstanceBuffers.lodFadesBuffer);
                }

                break;
            }
        }
    }
}