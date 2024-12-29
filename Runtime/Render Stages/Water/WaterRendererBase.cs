using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline.Water
{
    public abstract class WaterRendererBase : RenderFeature
    {
        protected BufferHandle indexBuffer;
        protected WaterSettings settings;
        protected int VerticesPerTileEdge => settings.PatchVertices + 1;
        protected int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;

        private GraphicsBuffer indexBufferInternal;

        public WaterRendererBase(RenderGraph renderGraph, WaterSettings settings) : base(renderGraph)
        {
            this.settings = settings;
            indexBufferInternal = new GraphicsBuffer(GraphicsBuffer.Target.Index, QuadListIndexCount, sizeof(ushort)) { name = "Water System Index Buffer" };

            var index = 0;
            var pIndices = new ushort[QuadListIndexCount];
            for (var y = 0; y < settings.PatchVertices; y++)
            {
                var rowStart = y * VerticesPerTileEdge;

                for (var x = 0; x < settings.PatchVertices; x++)
                {
                    // Can do a checkerboard flip to avoid directioanl artifacts, but will mess with the tessellation code
                    //var flip = (x & 1) == (y & 1);

                    //if(flip)
                    //{
                    pIndices[index++] = (ushort)(rowStart + x);
                    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge);
                    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                    pIndices[index++] = (ushort)(rowStart + x + 1);
                    //}
                    //else
                    //{
                    //    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge);
                    //    pIndices[index++] = (ushort)(rowStart + x + VerticesPerTileEdge + 1);
                    //    pIndices[index++] = (ushort)(rowStart + x + 1);
                    //    pIndices[index++] = (ushort)(rowStart + x);
                    //}
                }
            }

            indexBufferInternal.SetData(pIndices);
            indexBuffer = renderGraph.BufferHandleSystem.ImportResource(indexBufferInternal);
        }

        protected override void Cleanup(bool disposing)
        {
            indexBufferInternal.Dispose();
        }

        protected WaterCullResult Cull(Vector3 viewPosition, CullingPlanes cullingPlanes)
        {
            // TODO: Preload?
            var compute = Resources.Load<ComputeShader>("OceanQuadtreeCull");
            var indirectArgsBuffer = renderGraph.GetBuffer(5, target: GraphicsBuffer.Target.IndirectArguments);
            var patchDataBuffer = renderGraph.GetBuffer(settings.CellCount * settings.CellCount, target: GraphicsBuffer.Target.Structured);

            // We can do 32x32 cells in a single pass, larger counts need to be broken up into several passes
            var maxPassesPerDispatch = 6;
            var totalPassCount = (int)Mathf.Log(settings.CellCount, 2f) + 1;
            var dispatchCount = Mathf.Ceil(totalPassCount / (float)maxPassesPerDispatch);

            RTHandle tempLodId = default;
            BufferHandle lodIndirectArgsBuffer = default;
            if (dispatchCount > 1)
            {
                // If more than one dispatch, we need to write lods out to a temp texture first. Otherwise they are done via shared memory so no texture is needed
                tempLodId = renderGraph.GetTexture(settings.CellCount, settings.CellCount, GraphicsFormat.R16_UInt);
                lodIndirectArgsBuffer = renderGraph.GetBuffer(3, target: GraphicsBuffer.Target.IndirectArguments);
            }

            var tempIds = ListPool<RTHandle>.Get();
            for (var i = 0; i < dispatchCount - 1; i++)
            {
                var tempResolution = 1 << ((i + 1) * (maxPassesPerDispatch - 1));
                tempIds.Add(renderGraph.GetTexture(tempResolution, tempResolution, GraphicsFormat.R16_UInt));
            }

            for (var i = 0; i < dispatchCount; i++)
            {
                using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Quadtree Cull"))
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

                    // pass.AddKeyword("NO_HEIGHTS");

                    if (isFinalPass && !isFirstPass)
                    {
                        // Final pass writes out lods to a temp texture if more than one pass was used
                        pass.WriteTexture("_LodResult", tempLodId);
                    }

                    if (!isFinalPass)
                        pass.WriteTexture("_TempResultWrite", tempIds[i]);

                    pass.WriteBuffer("_IndirectArgs", indirectArgsBuffer);
                    pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);

                    pass.AddRenderPassData<ICommonPassData>();

                    var index = i;
                    pass.SetRenderFunction((System.Action<CommandBuffer, RenderPass>)((command, pass) =>
                    {
                        // First pass sets the buffer contents
                        if (isFirstPass)
                        {
                            var indirectArgs = ListPool<int>.Get();
                            indirectArgs.Add(QuadListIndexCount); // index count per instance
                            indirectArgs.Add(0); // instance count (filled in later)
                            indirectArgs.Add(0); // start index location
                            indirectArgs.Add(0); // base vertex location
                            indirectArgs.Add(0); // start instance location
                            command.SetBufferData(pass.GetBuffer(indirectArgsBuffer), indirectArgs);
                            ListPool<int>.Release(indirectArgs);
                        }

                        // Do up to 6 passes per dispatch.
                        pass.SetInt("_PassCount", (int)passCount);
                        pass.SetInt("_PassOffset", 6 * index);
                        pass.SetInt("_TotalPassCount", (int)totalPassCount);

                        var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                        for (var i = 0; i < cullingPlanes.Count; i++)
                            cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                        pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                        ArrayPool<Vector4>.Release(cullingPlanesArray);

                        // Snap to quad-sized increments on largest cell
                        var texelSize = settings.Size / (float)settings.PatchVertices;
                        var positionX = MathUtils.Snap(viewPosition.x, texelSize) - viewPosition.x - settings.Size * 0.5f;
                        var positionZ = MathUtils.Snap(viewPosition.z, texelSize) - viewPosition.z - settings.Size * 0.5f;

                        var positionOffset = new Vector4((float)settings.Size, (float)settings.Size, positionX, positionZ);
                        pass.SetVector("_TerrainPositionOffset", positionOffset);

                        pass.SetFloat("_EdgeLength", (float)settings.EdgeLength * settings.PatchVertices);
                        pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                        pass.SetFloat("MaxWaterHeight", settings.Profile.MaxWaterHeight);
                    }));
                }
            }

            if (dispatchCount > 1)
            {
                using (var pass = renderGraph.AddRenderPass<ComputeRenderPass>("Ocean Quadtree Cull"))
                {
                    pass.Initialize(compute, 1, normalizedDispatch: false);
                    pass.WriteBuffer("_IndirectArgs", lodIndirectArgsBuffer);

                    // If more than one pass needed, we need a second pass to write out lod deltas to the patch data
                    // Copy count from indirect draw args so we only dispatch as many threads as needed
                    pass.ReadBuffer("_IndirectArgsInput", indirectArgsBuffer);
                }

                using (var pass = renderGraph.AddRenderPass<IndirectComputeRenderPass>("Ocean Quadtree Cull"))
                {
                    pass.Initialize(compute, lodIndirectArgsBuffer, 2);
                    pass.WriteBuffer("_PatchDataWrite", patchDataBuffer);
                    pass.ReadTexture("_LodInput", tempLodId);
                    pass.ReadBuffer("_IndirectArgs", indirectArgsBuffer);

                    pass.SetRenderFunction((System.Action<CommandBuffer, RenderPass>)((command, pass) =>
                    {
                        pass.SetInt("_CellCount", (int)settings.CellCount);
                    }));
                }
            }

            ListPool<RTHandle>.Release(tempIds);
            return new(indirectArgsBuffer, patchDataBuffer);
        }
    }
}