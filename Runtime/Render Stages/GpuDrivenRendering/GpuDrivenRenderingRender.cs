using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class GpuDrivenRenderingRender : RenderFeature
    {
        private static readonly uint[] emptyCounter = new uint[1];

        private readonly ComputeShader clearShader, cullingShader, scanShader;

        public GpuDrivenRenderingRender(RenderGraph renderGraph) : base(renderGraph)
        {
            clearShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceClear");
            cullingShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstanceRendererCull");
            scanShader = Resources.Load<ComputeShader>("GpuInstancedRendering/InstancePrefixSum");
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

            var isShadow = false; // TODO: Support?
            var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
            for (var i = 0; i < cullingPlanes.Count; i++)
                cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

            var rendererVisibility = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Cull"))
            {
                pass.Initialize(cullingShader, 0, gpuInstanceBuffers.totalInstanceCount);

                if (!isShadow)
                    pass.AddKeyword("HIZ_ON");

                pass.WriteBuffer("_RendererInstanceIDs", rendererVisibility);
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
            scanShader.GetThreadGroupSizes(1, gpuInstanceBuffers.totalInstanceCount, out var groupsX);
            var groupSums = renderGraph.GetBuffer((int)groupsX);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Prefix Sum 1"))
            {
                pass.Initialize(scanShader, 0, gpuInstanceBuffers.totalInstanceCount);
                pass.WriteBuffer("PrefixSumsWrite", prefixSums);
                pass.WriteBuffer("GroupSumsWrite", groupSums);

                pass.ReadBuffer("Input", rendererVisibility);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("MaxThread", gpuInstanceBuffers.totalInstanceCount);
                });
            }

            // TODO: This only handles a 2048*1024 array for now
            var groupSums1 = renderGraph.GetBuffer((int)groupsX);
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Instance Prefix Sum 2"))
            {
                pass.Initialize(scanShader, 1, (int)groupsX);
                pass.WriteBuffer("PrefixSumsWrite", groupSums1);
                pass.WriteBuffer("DrawCallArgsWrite", gpuInstanceBuffers.drawCallArgsBuffer);
                pass.ReadBuffer("Input", groupSums);

                pass.SetRenderFunction((command, pass) =>
                {
                    pass.SetInt("MaxThread", (int)groupsX);
                });
            }

            var objectToWorld = renderGraph.GetBuffer(gpuInstanceBuffers.totalInstanceCount, UnsafeUtility.SizeOf<Matrix3x4>());
            using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Stream Compaction"))
            {
                pass.Initialize(scanShader, 2, gpuInstanceBuffers.totalInstanceCount);
                pass.WriteBuffer("_ObjectToWorldWrite", objectToWorld);

                pass.ReadBuffer("Input", rendererVisibility);
                pass.ReadBuffer("_Positions", gpuInstanceBuffers.positionsBuffer);
                pass.ReadBuffer("PrefixSums", prefixSums);
                pass.ReadBuffer("GroupSums", groupSums1);

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

                    pass.ReadBuffer("_VisibleRendererInstanceIndices", rendererVisibility);
                    pass.ReadBuffer("_ObjectToWorld", objectToWorld);
                    pass.ReadBuffer("_InstancePositions", gpuInstanceBuffers.positionsBuffer);
                    pass.ReadBuffer("_InstanceLodFades", gpuInstanceBuffers.lodFadesBuffer);
                }

                break;
            }
        }
    }
}