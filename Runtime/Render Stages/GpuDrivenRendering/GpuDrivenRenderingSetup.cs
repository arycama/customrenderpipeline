using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class GpuDrivenRenderingSetup : RenderFeature
    {
        private readonly ComputeShader fillInstanceTypeIdShader;
        private ResourceHandle<GraphicsBuffer> rendererBoundsBuffer, rendererCountsBuffer, finalRendererCountsBuffer, submeshOffsetLengthsBuffer, lodSizesBuffer, rendererInstanceIDsBuffer, instanceTypeIdsBuffer, instanceTypeDataBuffer, instanceTypeLodDataBuffer, rendererInstanceIndexOffsetsBuffer, visibleRendererInstanceIndicesBuffer, positionsBuffer, lodFadesBuffer, drawCallArgsBuffer;

        private Dictionary<string, List<RendererDrawCallData>> passDrawList = new();

        private readonly ProceduralGenerationController proceduralGenerationController;
        private bool hasGenerated;

        public GpuDrivenRenderingSetup(RenderGraph renderGraph, ProceduralGenerationController proceduralGenerationController) : base(renderGraph)
        {
            fillInstanceTypeIdShader = Resources.Load<ComputeShader>("GpuInstancedRendering/FillInstanceTypeId");
            this.proceduralGenerationController = proceduralGenerationController;
        }

        protected override void Cleanup(bool disposing)
        {
            renderGraph.ReleasePersistentResource(drawCallArgsBuffer);
            renderGraph.ReleasePersistentResource(rendererInstanceIndexOffsetsBuffer);
            renderGraph.ReleasePersistentResource(rendererBoundsBuffer);
            renderGraph.ReleasePersistentResource(rendererCountsBuffer);
            renderGraph.ReleasePersistentResource(finalRendererCountsBuffer);
            renderGraph.ReleasePersistentResource(submeshOffsetLengthsBuffer);
            renderGraph.ReleasePersistentResource(lodSizesBuffer);
            renderGraph.ReleasePersistentResource(rendererInstanceIDsBuffer);
            renderGraph.ReleasePersistentResource(visibleRendererInstanceIndicesBuffer);
            renderGraph.ReleasePersistentResource(instanceTypeIdsBuffer);
            renderGraph.ReleasePersistentResource(lodFadesBuffer);
            renderGraph.ReleasePersistentResource(positionsBuffer);
            renderGraph.ReleasePersistentResource(instanceTypeDataBuffer);
            renderGraph.ReleasePersistentResource(instanceTypeLodDataBuffer);
        }

        public override void Render()
        {
            if (hasGenerated || !proceduralGenerationController.IsReady)
                return;

            hasGenerated = true;
            proceduralGenerationController.FreeUnusedHandles(renderGraph);

            // Fill instanceId buffer. (Should be done when the object is assigned)
            // This buffer contains the type at each index. (Eg 0, 1, 2)
            var positionCountSum = 0;
            foreach (var data in proceduralGenerationController.instanceData)
                positionCountSum += data.totalCount;

            instanceTypeIdsBuffer = renderGraph.GetBuffer(positionCountSum, isPersistent: true);
            positionsBuffer = renderGraph.GetBuffer(positionCountSum, sizeof(float) * 12, isPersistent: true);
            lodFadesBuffer = renderGraph.GetBuffer(positionCountSum, isPersistent: true);

            // TODO: We need to convert from the indices written in the original pass to the final rendering indices
            var positionOffset = 0;
            foreach (var data in proceduralGenerationController.instanceData)
            {
                using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Gpu Driven Rendering FillBuffers"))
                {
                    pass.Initialize(fillInstanceTypeIdShader, 0, data.totalCount);

                    pass.ReadBuffer("_PositionsInput", data.positions);
                    pass.ReadBuffer("_InstanceTypeIdsInput", data.instanceId);

                    pass.WriteBuffer("_InstanceTypeIds", instanceTypeIdsBuffer);
                    pass.WriteBuffer("_PositionsResult", positionsBuffer);
                    pass.WriteBuffer("_LodFadesResult", lodFadesBuffer);

                    pass.SetRenderFunction((positionOffset, data.totalCount), (command, pass, data) =>
                    {
                        pass.SetInt("_Offset", data.positionOffset);
                        pass.SetInt("_Count", data.totalCount);
                    });

                    positionOffset += data.totalCount;

                    renderGraph.ReleasePersistentResource(data.positions);
                    renderGraph.ReleasePersistentResource(data.instanceId);
                }
            }

            // Build mesh rendering data
            using var sharedMaterials = ScopedPooledList<Material>.Get();
            using var renderers = ScopedPooledList<Renderer>.Get();

            // Note these are used to set data inside the lambda, so can't have a using statement
            var submeshOffsetLengths = ScopedPooledList<Vector2Int>.Get();
            var lodSizes = ScopedPooledList<float>.Get();
            var rendererBounds = ScopedPooledList<RendererBounds>.Get();
            var drawCallArgs = ScopedPooledList<DrawIndexedInstancedIndirectArgs>.Get();
            var instanceTypeDatas = ScopedPooledList<InstanceTypeData>.Get();
            var instanceTypeLodDatas = ScopedPooledList<InstanceTypeLodData>.Get();

            var submeshOffset = 0;
            var lodOffset = 0;
            var instanceTimesRendererCount = 0;
            var totalRendererSum = 0;

            // Stores the starting thread for each instance position
            var totalInstanceCount = 0;
            var indirectArgsOffset = 0;

            passDrawList.Clear();

            foreach (var prefab in proceduralGenerationController.prefabs)
            {
                var prefabInstanceCount = prefab.count;

                InstanceTypeData typeData;
                typeData.instanceCount = prefabInstanceCount;
                typeData.lodRendererOffset = lodOffset;

                if (prefab.prefab.TryGetComponent<LODGroup>(out var lodGroup))
                {
                    typeData.localReferencePoint = lodGroup.localReferencePoint;
                    typeData.radius = lodGroup.size * 0.5f;
                    typeData.lodCount = lodGroup.lodCount;
                    typeData.lodSizeBufferPosition = lodOffset;

                    var lods = lodGroup.GetLODs();

                    foreach (var lod in lods)
                    {
                        ProcessLodLevel(lod.renderers, lod.screenRelativeTransitionHeight / QualitySettings.lodBias);
                    }
                }
                else
                {
                    prefab.prefab.GetComponentsInChildren<Renderer>(renderers);
                    var bounds = renderers.Value[0].bounds;
                    for (var i = 1; i < renderers.Value.Count; i++)
                    {
                        bounds.Encapsulate(renderers.Value[i].bounds);
                    }

                    typeData.localReferencePoint = bounds.center;
                    typeData.radius = Vector3.Magnitude(bounds.extents);
                    typeData.lodCount = 1;
                    typeData.lodSizeBufferPosition = lodOffset;

                    ProcessLodLevel(renderers.Value, 0f);
                }

                void ProcessLodLevel(IList<Renderer> renderers, float lodSize)
                {
                    foreach (var renderer in renderers)
                    {
                        if (renderer == null)
                            continue;

                        var meshFilter = renderer.GetComponent<MeshFilter>();
                        if (meshFilter == null)
                            continue;

                        var mesh = meshFilter.sharedMesh;
                        if (mesh == null)
                            continue;

                        var rendererHasMotionVectors = renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Object;
                        var rendererIsShadowCaster = renderer.shadowCastingMode != ShadowCastingMode.Off;

                        // TODO: Once we have combined mesh support, we should just transform the vertices by the matrix to avoid extra shader work
                        var localToWorld = Matrix4x4.TRS(renderer.transform.localPosition, renderer.transform.localRotation, renderer.transform.localScale);

                        renderer.GetSharedMaterials(sharedMaterials);

                        submeshOffsetLengths.Value.Add(new Vector2Int(submeshOffset, sharedMaterials.Value.Count));
                        submeshOffset += sharedMaterials.Value.Count;

                        // Get the mesh bounds, and transform by the renderer's matrix if it is not identity
                        var bounds = mesh.bounds;
                        if (localToWorld != Matrix4x4.identity)
                            bounds = bounds.Transform(localToWorld);

                        rendererBounds.Value.Add(new RendererBounds(bounds));

                        for (var i = 0; i < sharedMaterials.Value.Count; i++)
                        {
                            var material = sharedMaterials.Value[i];
                            if (material == null)
                                continue;

                            // First, find if the material has a motion vectors pass
                            var materialHasMotionVectors = false;
                            if (rendererHasMotionVectors)
                            {
                                for (var j = 0; j < material.passCount; j++)
                                {
                                    if (material.GetPassName(j) != "MotionVectors")
                                        continue;

                                    materialHasMotionVectors = true;
                                    break;
                                }
                            }

                            // Now add any valid passes. If material has motion vectors enabled, no other passes will be added except shadows.
                            for (var j = 0; j < material.passCount; j++)
                            {
                                var passName = material.GetPassName(j);

                                // Skip MotionVectors passes if not enabled
                                if (!materialHasMotionVectors && passName == "MotionVectors")
                                    continue;

                                // Skip ShadowCaster passes if shadows not enabled
                                if (!rendererIsShadowCaster && passName == "ShadowCaster")
                                    continue;

                                // Skip non-motion vector passes if motion vectors enabled (Except shadow caster passes)
                                if (materialHasMotionVectors && passName != "MotionVectors" && passName != "ShadowCaster")
                                    continue;

                                // Get the draw list for the current pass, or create if it doesn't yet exist
                                if (!passDrawList.TryGetValue(passName, out var drawList))
                                {
                                    drawList = new List<RendererDrawCallData>();
                                    passDrawList.Add(passName, drawList);
                                }

                                var drawData = new RendererDrawCallData(material.renderQueue, mesh, i, material, j, indirectArgsOffset * sizeof(uint), totalRendererSum, localToWorld);
                                drawList.Add(drawData);
                            }

                            var indexCount = meshFilter.sharedMesh.GetIndexCount(i);
                            var indexStart = meshFilter.sharedMesh.GetIndexStart(i);
                            drawCallArgs.Value.Add(new DrawIndexedInstancedIndirectArgs(indexCount, 0, indexStart, 0, 0));
                            indirectArgsOffset += 5;
                        }
                    }

                    instanceTypeLodDatas.Value.Add(new InstanceTypeLodData(totalRendererSum, renderers.Count, instanceTimesRendererCount - totalInstanceCount));

                    lodOffset++;
                    totalRendererSum += renderers.Count;

                    instanceTimesRendererCount += renderers.Count * prefabInstanceCount;
                    lodSizes.Value.Add(lodSize);
                }

                totalInstanceCount += prefabInstanceCount;
                instanceTypeDatas.Value.Add(typeData);
            }

            // Now that all the renderers are grouped, sort them by queue
            foreach (var item in passDrawList.Values)
            {
                item.Sort((draw0, draw1) => draw0.renderQueue.CompareTo(draw1.renderQueue));
            }

            using (var pass = renderGraph.AddRenderPass<GlobalRenderPass>("Gpu Driven Rendering Fill Buffers"))
            {
                submeshOffsetLengthsBuffer = renderGraph.GetBuffer(submeshOffsetLengths.Value.Count, sizeof(uint) * 2, isPersistent: true);
                lodSizesBuffer = renderGraph.GetBuffer(lodSizes.Value.Count, sizeof(float), isPersistent: true);
                rendererBoundsBuffer = renderGraph.GetBuffer(rendererBounds.Value.Count, UnsafeUtility.SizeOf<RendererBounds>(), isPersistent: true);
                instanceTypeDataBuffer = renderGraph.GetBuffer(instanceTypeDatas.Value.Count, UnsafeUtility.SizeOf<InstanceTypeData>(), isPersistent: true);
                drawCallArgsBuffer = renderGraph.GetBuffer(drawCallArgs.Value.Count, UnsafeUtility.SizeOf<DrawIndexedInstancedIndirectArgs>(), GraphicsBuffer.Target.IndirectArguments, isPersistent: true);
                instanceTypeLodDataBuffer = renderGraph.GetBuffer(instanceTypeLodDatas.Value.Count, UnsafeUtility.SizeOf<InstanceTypeLodData>(), isPersistent: true);

                pass.WriteBuffer("", submeshOffsetLengthsBuffer);
                pass.WriteBuffer("", lodSizesBuffer);
                pass.WriteBuffer("", rendererBoundsBuffer);
                pass.WriteBuffer("", instanceTypeDataBuffer);
                pass.WriteBuffer("", drawCallArgsBuffer);
                pass.WriteBuffer("", instanceTypeLodDataBuffer);

                pass.SetRenderFunction((command, pass) =>
                {
                    // TODO: Use lockbuffer?
                    command.SetBufferData(pass.GetBuffer(submeshOffsetLengthsBuffer), submeshOffsetLengths.Value);
                    command.SetBufferData(pass.GetBuffer(lodSizesBuffer), lodSizes.Value);
                    command.SetBufferData(pass.GetBuffer(rendererBoundsBuffer), rendererBounds.Value);
                    command.SetBufferData(pass.GetBuffer(instanceTypeDataBuffer), instanceTypeDatas.Value);
                    command.SetBufferData(pass.GetBuffer(drawCallArgsBuffer), drawCallArgs.Value);
                    command.SetBufferData(pass.GetBuffer(instanceTypeLodDataBuffer), instanceTypeLodDatas.Value);

                    submeshOffsetLengths.Dispose();
                    lodSizes.Dispose();
                    rendererBounds.Dispose();
                    drawCallArgs.Dispose();
                    instanceTypeDatas.Dispose();
                    instanceTypeLodDatas.Dispose();
                });
            }

            rendererInstanceIDsBuffer = renderGraph.GetBuffer(instanceTimesRendererCount, isPersistent: true);
            visibleRendererInstanceIndicesBuffer = renderGraph.GetBuffer(instanceTimesRendererCount, isPersistent: true);
            rendererCountsBuffer = renderGraph.GetBuffer(totalRendererSum, isPersistent: true);
            rendererInstanceIndexOffsetsBuffer = renderGraph.GetBuffer(totalRendererSum, isPersistent: true);
            finalRendererCountsBuffer = renderGraph.GetBuffer(totalRendererSum, isPersistent: true);

            renderGraph.SetResource(new GpuInstanceBuffersData(rendererInstanceIDsBuffer, rendererInstanceIndexOffsetsBuffer, rendererCountsBuffer, finalRendererCountsBuffer, visibleRendererInstanceIndicesBuffer, positionsBuffer, instanceTypeIdsBuffer, lodFadesBuffer, rendererBoundsBuffer, lodSizesBuffer, instanceTypeDataBuffer, instanceTypeLodDataBuffer, submeshOffsetLengthsBuffer, drawCallArgsBuffer, passDrawList, totalInstanceCount, instanceTimesRendererCount, totalRendererSum), true);
        }
    }
}