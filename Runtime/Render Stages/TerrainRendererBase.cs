using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;

namespace Arycama.CustomRenderPipeline
{
    public abstract partial class TerrainRendererBase : RenderFeature
    {
        protected readonly TerrainSettings settings;
        protected int VerticesPerTileEdge => settings.PatchVertices + 1;
        protected int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

        protected TerrainRendererBase(RenderGraph renderGraph, TerrainSettings settings) : base(renderGraph)
        {
            this.settings = settings;
        }

        protected CullResult Cull(Vector3 viewPosition, CullingPlanes cullingPlanes)
        {
            // TODO: Preload?
            var compute = Resources.Load<ComputeShader>("Terrain/TerrainQuadtreeCull");
            var indirectArgsBuffer = renderGraph.GetBuffer(5, target: GraphicsBuffer.Target.IndirectArguments);
            var patchDataBuffer = renderGraph.GetBuffer(settings.CellCount * settings.CellCount, target: GraphicsBuffer.Target.Structured);

            // We can do 32x32 cells in a single pass, larger counts need to be broken up into several passes
            var maxPassesPerDispatch = 6;
            var totalPassCount = (int)Mathf.Log(settings.CellCount, 2f) + 1;
            var dispatchCount = Mathf.Ceil(totalPassCount / (float)maxPassesPerDispatch);

            ResourceHandle<RenderTexture> tempLodId = default;
            ResourceHandle<GraphicsBuffer> lodIndirectArgsBuffer = default;
            if (dispatchCount > 1)
            {
                // If more than one dispatch, we need to write lods out to a temp texture first. Otherwise they are done via shared memory so no texture is needed
                tempLodId = renderGraph.GetTexture(settings.CellCount, settings.CellCount, GraphicsFormat.R16_UInt);
                lodIndirectArgsBuffer = renderGraph.GetBuffer(3, target: GraphicsBuffer.Target.IndirectArguments);
            }

            var tempIds = ListPool<ResourceHandle<RenderTexture>>.Get();
            for (var i = 0; i < dispatchCount - 1; i++)
            {
                var tempResolution = 1 << ((i + 1) * (maxPassesPerDispatch - 1));
                tempIds.Add(renderGraph.GetTexture(tempResolution, tempResolution, GraphicsFormat.R16_UInt));
            }

            for (var i = 0; i < dispatchCount; i++)
            {
                using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Quadtree Cull"))
                {
                    var isFirstPass = i == 0; // Also indicates whether this is -not- the first pass
                    if (!isFirstPass)
                        pass.ReadTexture("_TempResult", tempIds[i - 1]);

                    var isFinalPass = i == dispatchCount - 1; // Also indicates whether this is -not- the final pass

                    var passCount = Mathf.Min(maxPassesPerDispatch, totalPassCount - i * maxPassesPerDispatch);
                    var threadCount = 1 << (i * 6 + passCount - 1);
                    pass.Initialize(compute, 0, threadCount, threadCount);

                    if (isFirstPass)
                        pass.AddKeyword("FIRST");

                    if (isFinalPass)
                        pass.AddKeyword("FINAL");

                    if (isFinalPass && !isFirstPass)
                    {
                        // Final pass writes out lods to a temp texture if more than one pass was used
                        pass.WriteTexture("_LodResult", tempLodId);
                    }

                    if (!isFinalPass)
                        pass.WriteTexture("_TempResultWrite", tempIds[i]);

                    var terrainSystemData = renderGraph.GetResource<TerrainSystemData>();

                    pass.WriteBuffer("_IndirectArgs", indirectArgsBuffer);
                    pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
                    pass.ReadTexture("_TerrainHeights", terrainSystemData.MinMaxHeights);
                    pass.AddRenderPassData<ICommonPassData>();

                    var index = i;
                    pass.SetRenderFunction(
                    (
                        viewPosition,
                        cullingPlanes,
                        indirectArgsBuffer,
                        totalPassCount,
                        isFirstPass,
                        passCount,
                        index,
                        QuadListIndexCount,
                        terrainSystemData.Terrain,
                        terrainSystemData.TerrainData,
                        settings
                    ),
                    (command, pass, data) =>
                    {
                        // First pass sets the buffer contents
                        if (data.isFirstPass)
                        {
                            var indirectArgs = ListPool<int>.Get();
                            indirectArgs.Add(data.QuadListIndexCount); // index count per instance
                            indirectArgs.Add(0); // instance count (filled in later)
                            indirectArgs.Add(0); // start index location
                            indirectArgs.Add(0); // base vertex location
                            indirectArgs.Add(0); // start instance location
                            command.SetBufferData(pass.GetBuffer(data.indirectArgsBuffer), indirectArgs);
                            ListPool<int>.Release(indirectArgs);
                        }

                        // Do up to 6 passes per dispatch.
                        pass.SetInt("_PassCount", data.passCount);
                        pass.SetInt("_PassOffset", 6 * data.index);
                        pass.SetInt("_TotalPassCount", data.totalPassCount);

                        var cullingPlanesArray = ArrayPool<Vector4>.Get(data.cullingPlanes.Count);
                        for (var i = 0; i < data.cullingPlanes.Count; i++)
                            cullingPlanesArray[i] = data.cullingPlanes.GetCullingPlaneVector4(i);

                        pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                        ArrayPool<Vector4>.Release(cullingPlanesArray);

                        // Snap to quad-sized increments on largest cell
                        var position = data.Terrain.GetPosition() - data.viewPosition;
                        var positionOffset = new Vector4(data.TerrainData.size.x, data.TerrainData.size.z, position.x, position.z);
                        pass.SetVector("_TerrainPositionOffset", positionOffset);

                        pass.SetFloat("_EdgeLength", (float)data.settings.EdgeLength * data.settings.PatchVertices);
                        pass.SetInt("_CullingPlanesCount", data.cullingPlanes.Count);

                        pass.SetFloat("_InputScale", data.TerrainData.size.y);
                        pass.SetFloat("_InputOffset", position.y);

                        pass.SetInt("_MipCount", Texture2DExtensions.MipCount(data.TerrainData.heightmapResolution) - 1);
                    });
                }
            }

            if (dispatchCount > 1)
            {
                using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Terrain Quadtree Cull"))
                {
                    pass.Initialize(compute, 1, normalizedDispatch: false);
                    pass.WriteBuffer("_IndirectArgs", lodIndirectArgsBuffer);

                    // If more than one pass needed, we need a second pass to write out lod deltas to the patch data
                    // Copy count from indirect draw args so we only dispatch as many threads as needed
                    pass.ReadBuffer("_IndirectArgsInput", indirectArgsBuffer);
                }

                using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Terrain Quadtree Cull"))
                {
                    pass.Initialize(compute, lodIndirectArgsBuffer, 2);
                    pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
                    pass.ReadTexture("_LodInput", tempLodId);
                    pass.ReadBuffer("_IndirectArgs", indirectArgsBuffer);

                    pass.SetRenderFunction((command, pass) =>
                    {
                        pass.SetInt("_CellCount", settings.CellCount);
                    });
                }
            }

            ListPool<ResourceHandle<RenderTexture>>.Release(tempIds);

            return new(indirectArgsBuffer, patchDataBuffer);
        }
    }
}