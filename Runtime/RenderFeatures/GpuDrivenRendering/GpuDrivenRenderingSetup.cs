using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

public class GpuDrivenRenderingSetup : FrameRenderFeature
{
    private readonly ComputeShader fillInstanceTypeIdShader;
    private ResourceHandle<GraphicsBuffer> lodSizesBuffer, instanceTypeIdsBuffer, instanceTypeDataBuffer, instanceTypeLodDataBuffer, positionsBuffer, lodFadesBuffer, drawCallArgsBuffer, instanceBoundsBuffer, rendererLodIndicesBuffer;

    private Dictionary<string, List<RendererDrawCallData>> passDrawList = new();

    private readonly ProceduralGenerationController proceduralGenerationController;
    private int version = -1;
	private bool isInitialized;

    public GpuDrivenRenderingSetup(RenderGraph renderGraph, ProceduralGenerationController proceduralGenerationController) : base(renderGraph)
    {
        fillInstanceTypeIdShader = Resources.Load<ComputeShader>("GpuInstancedRendering/FillInstanceTypeId");
        this.proceduralGenerationController = proceduralGenerationController;
    }

    protected override void Cleanup(bool disposing)
    {
		if (!isInitialized)
			return;

        renderGraph.ReleasePersistentResource(drawCallArgsBuffer);
        renderGraph.ReleasePersistentResource(lodSizesBuffer);
        renderGraph.ReleasePersistentResource(instanceTypeIdsBuffer);
        renderGraph.ReleasePersistentResource(lodFadesBuffer);
        renderGraph.ReleasePersistentResource(positionsBuffer);
        renderGraph.ReleasePersistentResource(instanceTypeDataBuffer);
        renderGraph.ReleasePersistentResource(instanceTypeLodDataBuffer);
        renderGraph.ReleasePersistentResource(instanceBoundsBuffer);
        renderGraph.ReleasePersistentResource(rendererLodIndicesBuffer);
    }

    public override void Render(ScriptableRenderContext context)
    {
        if (version == proceduralGenerationController.Version)
            return;

        // If we have existing data, make sure to clean it up
        if(version != -1)
            Cleanup(false);

        version = proceduralGenerationController.Version;
        proceduralGenerationController.FreeUnusedHandles(renderGraph);

        // Fill instanceId buffer. (Should be done when the object is assigned)
        // This buffer contains the type at each index. (Eg 0, 1, 2)
        var positionCountSum = 0;
        foreach (var data in proceduralGenerationController.instanceData)
            positionCountSum += data.totalCount;

		if (positionCountSum == 0)
		{
			renderGraph.ClearResource<GpuDrivenRenderingData>();
			isInitialized = false;
			return;
		}

		isInitialized = true;

		instanceTypeIdsBuffer = renderGraph.GetBuffer(positionCountSum, isPersistent: true);
        positionsBuffer = renderGraph.GetBuffer(positionCountSum, sizeof(float) * 12, isPersistent: true);
        lodFadesBuffer = renderGraph.GetBuffer(positionCountSum, isPersistent: true);
        instanceBoundsBuffer = renderGraph.GetBuffer(positionCountSum, sizeof(float) * 4, isPersistent: true);

        // Need to compute bounds for each instance
        var boundsData = new List<RendererBounds>();
        foreach (var prefab in proceduralGenerationController.prefabs)
        {
            var childRenderers = prefab.prefab.GetComponentsInChildren<MeshRenderer>();
            if (childRenderers.Length == 0)
                continue;

            var bounds = childRenderers[0].bounds;
            for(var i = 1; i < childRenderers.Length; i++)
                bounds.Encapsulate(childRenderers[i].bounds);

            boundsData.Add(new RendererBounds(bounds));
        }

        // TODO: Use lock buffer?
        var instanceBoundsBufferTemp = renderGraph.GetBuffer(proceduralGenerationController.prefabs.Count, UnsafeUtility.SizeOf<RendererBounds>());

        // TODO: We need to convert from the indices written in the original pass to the final rendering indices
        var positionOffset = 0;
        foreach (var data in proceduralGenerationController.instanceData)
        {
            using (var pass = renderGraph.AddComputeRenderPass("Gpu Driven Rendering FillBuffers", (positionOffset, data.totalCount, instanceBoundsBufferTemp, boundsData)))
            {
                pass.Initialize(fillInstanceTypeIdShader, 0, data.totalCount);

                pass.WriteBuffer("_InstanceTypeIds", instanceTypeIdsBuffer);
                pass.WriteBuffer("_PositionsResult", positionsBuffer);
                pass.WriteBuffer("_LodFadesResult", lodFadesBuffer);
                pass.WriteBuffer("_InstanceBounds", instanceBoundsBuffer);
                pass.WriteBuffer("_InstanceTypeBounds", instanceBoundsBufferTemp);

                pass.ReadBuffer("_PositionsInput", data.positions);
                pass.ReadBuffer("_InstanceTypeIdsInput", data.instanceId);
                pass.ReadBuffer("_InstanceTypeBounds", instanceBoundsBufferTemp);

                pass.SetRenderFunction(static (command, pass, data) =>
                {
                    pass.SetInt("_Offset", data.positionOffset);
                    pass.SetInt("_Count", data.totalCount);

                    command.SetBufferData(pass.GetBuffer(data.instanceBoundsBufferTemp), data.boundsData);
                });

                positionOffset += data.totalCount;

                renderGraph.ReleasePersistentResource(data.positions);
                renderGraph.ReleasePersistentResource(data.instanceId);
            }
        }

		proceduralGenerationController.instanceData.Clear();

		// Build mesh rendering data
		using var sharedMaterials = ScopedPooledList<Material>.Get();
        using var renderers = ScopedPooledList<Renderer>.Get();

        // Note these are used to set data inside the lambda, so can't have a using statement
        var lodSizes = ScopedPooledList<float>.Get();
        var drawCallArgs = ScopedPooledList<IndirectDrawIndexedArgs>.Get();
        var instanceTypeDatas = ScopedPooledList<InstanceTypeData>.Get();
        var instanceTypeLodDatas = ScopedPooledList<InstanceTypeLodData>.Get();
		var rendererLodIndices = ScopedPooledList<uint>.Get();

        var totalLodCount = 0;
        var instanceTimesRendererCount = 0;
        var totalRendererCount = 0;

        // Stores the starting thread for each instance position
        var totalInstanceCount = 0;
        var indirectArgsOffset = 0;

        passDrawList.Clear();

        foreach (var prefab in proceduralGenerationController.prefabs)
        {
            InstanceTypeData typeData;
            typeData.instanceCount = prefab.count;
            typeData.lodRendererOffset = totalLodCount;
            typeData.lodSizeBufferPosition = totalLodCount;

            if (prefab.prefab.TryGetComponent<LODGroup>(out var lodGroup))
            {
                typeData.localReferencePoint = lodGroup.localReferencePoint;
                typeData.radius = lodGroup.size * 0.5f;
                typeData.lodCount = lodGroup.lodCount;

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

                ProcessLodLevel(renderers.Value, 0f);
            }

            void ProcessLodLevel(IList<Renderer> renderers, float lodSize)
            {
                var rendererCount = 0;
                foreach (var renderer in renderers)
                {
                    if (renderer == null)
                        continue;

                    if (!renderer.TryGetComponent<MeshFilter>(out var meshFilter))
                        continue;

                    var mesh = meshFilter.sharedMesh;
                    if (mesh == null)
                        continue;

                    var rendererHasMotionVectors = renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Object;
                    var rendererIsShadowCaster = renderer.shadowCastingMode != ShadowCastingMode.Off;

                    // TODO: Once we have combined mesh support, we should just transform the vertices by the matrix to avoid extra shader work
                    var localToWorld = Matrix4x4.TRS(renderer.transform.localPosition, renderer.transform.localRotation, renderer.transform.localScale);

                    renderer.GetSharedMaterials(sharedMaterials);

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

                            var drawData = new RendererDrawCallData(material.renderQueue, mesh, i, material, j, indirectArgsOffset * sizeof(uint), totalRendererCount, totalLodCount, (Float3x4)localToWorld);
                            drawList.Add(drawData);
						}

                        var indexCount = meshFilter.sharedMesh.GetIndexCount(i);
                        var indexStart = meshFilter.sharedMesh.GetIndexStart(i);
                        drawCallArgs.Value.Add(new IndirectDrawIndexedArgs(indexCount, 0, indexStart, 0, 0));
                        indirectArgsOffset += 5;
						rendererCount++;

						// Stores a mapping from a renderer to it's lod so we can quickly look it up in the shader
						rendererLodIndices.Value.Add((uint)totalLodCount);
					}
				}

                instanceTypeLodDatas.Value.Add(new InstanceTypeLodData(totalRendererCount, rendererCount, instanceTimesRendererCount - totalInstanceCount));

                totalLodCount++; // TODO: Could simply be incremented once per group
                totalRendererCount += rendererCount;

                instanceTimesRendererCount += rendererCount * prefab.count;
                lodSizes.Value.Add(lodSize);
            }

            totalInstanceCount += prefab.count;
            instanceTypeDatas.Value.Add(typeData);
        }

        // Now that all the renderers are grouped, sort them by queue
        foreach (var item in passDrawList.Values)
        {
            item.Sort((draw0, draw1) => draw0.renderQueue.CompareTo(draw1.renderQueue));
        }

		lodSizesBuffer = renderGraph.GetBuffer(lodSizes.Value.Count, sizeof(float), isPersistent: true);
		instanceTypeDataBuffer = renderGraph.GetBuffer(instanceTypeDatas.Value.Count, UnsafeUtility.SizeOf<InstanceTypeData>(), isPersistent: true);
		drawCallArgsBuffer = renderGraph.GetBuffer(drawCallArgs.Value.Count, UnsafeUtility.SizeOf<IndirectDrawIndexedArgs>(), GraphicsBuffer.Target.IndirectArguments, isPersistent: true);
		instanceTypeLodDataBuffer = renderGraph.GetBuffer(instanceTypeLodDatas.Value.Count, UnsafeUtility.SizeOf<InstanceTypeLodData>(), isPersistent: true);
		rendererLodIndicesBuffer = renderGraph.GetBuffer(rendererLodIndices.Value.Count, isPersistent: true);

		using (var pass = renderGraph.AddGenericRenderPass("Gpu Driven Rendering Fill Buffers", (lodSizesBuffer, lodSizes, instanceTypeDataBuffer, instanceTypeDatas, drawCallArgsBuffer, drawCallArgs, instanceTypeLodDataBuffer, instanceTypeLodDatas, rendererLodIndicesBuffer, rendererLodIndices)))
		{
			pass.WriteBuffer("", lodSizesBuffer);
			pass.WriteBuffer("", instanceTypeDataBuffer);
			pass.WriteBuffer("", drawCallArgsBuffer);
			pass.WriteBuffer("", instanceTypeLodDataBuffer);
			pass.WriteBuffer("", rendererLodIndicesBuffer);

			pass.SetRenderFunction(static (command, pass, data) =>
			{
				// TODO: Use lockbuffer?
				command.SetBufferData(pass.GetBuffer(data.lodSizesBuffer), data.lodSizes.Value);
				command.SetBufferData(pass.GetBuffer(data.instanceTypeDataBuffer), data.instanceTypeDatas.Value);
				command.SetBufferData(pass.GetBuffer(data.drawCallArgsBuffer), data.drawCallArgs.Value);
				command.SetBufferData(pass.GetBuffer(data.instanceTypeLodDataBuffer), data.instanceTypeLodDatas.Value);

				var b = pass.GetBuffer(data.rendererLodIndicesBuffer);
				command.SetBufferData(b, data.rendererLodIndices.Value);

				data.lodSizes.Dispose();
				data.drawCallArgs.Dispose();
				data.instanceTypeDatas.Dispose();
				data.instanceTypeLodDatas.Dispose();
				data.rendererLodIndices.Dispose();
			});
		}

        renderGraph.SetResource(new GpuDrivenRenderingData(positionsBuffer, instanceTypeIdsBuffer, lodFadesBuffer, lodSizesBuffer, instanceTypeDataBuffer, instanceTypeLodDataBuffer, drawCallArgsBuffer, instanceBoundsBuffer, rendererLodIndicesBuffer, passDrawList, totalInstanceCount, totalRendererCount, totalLodCount), true);
    }
}