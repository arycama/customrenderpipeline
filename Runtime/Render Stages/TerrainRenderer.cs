using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public class TerrainRenderer : RenderFeature
    {
        private readonly TerrainSystem terrainSystem;
        private readonly TerrainSystem.Settings settings;

        public TerrainRenderer(TerrainSystem terrainSystem, TerrainSystem.Settings settings, RenderGraph renderGraph) : base(renderGraph)
        {
            this.terrainSystem = terrainSystem ?? throw new ArgumentNullException(nameof(terrainSystem));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public override void Render()
        {
            if (terrainSystem.terrain == null || settings.Material == null)
                return;

            var viewData = renderGraph.GetResource<ViewData>();
            var cullingPlanes = renderGraph.GetResource<CullingPlanesData>().CullingPlanes;

            var passData = terrainSystem.Cull(viewData.ViewPosition, cullingPlanes);
            var passIndex = settings.Material.FindPass("Terrain");
            Assert.IsFalse(passIndex == -1, "Terrain Material has no Terrain Pass");

            var size = terrainSystem.terrainData.size;
            var position = terrainSystem.terrain.GetPosition() - viewData.ViewPosition;

            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Terrain Render"))
            {
                pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.None, RenderBufferLoadAction.DontCare);

                pass.Initialize(settings.Material, terrainSystem.indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex);
                pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);

                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
                pass.AddRenderPassData<TerrainRenderData>();
                pass.AddRenderPassData<ICommonPassData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    var VerticesPerTileEdge = terrainSystem.VerticesPerTileEdge;
                    pass.SetInt("_VerticesPerEdge", VerticesPerTileEdge);
                    pass.SetInt("_VerticesPerEdgeMinusOne", VerticesPerTileEdge - 1);
                    pass.SetFloat("_RcpVerticesPerEdge", 1f / VerticesPerTileEdge);
                    pass.SetFloat("_RcpVerticesPerEdgeMinusOne", 1f / (VerticesPerTileEdge - 1));

                    var scaleOffset = new Vector4(size.x / settings.CellCount, size.z / settings.CellCount, position.x, position.z);
                    pass.SetVector("_PatchScaleOffset", scaleOffset);
                    pass.SetVector("_SpacingScale", new Vector4(size.x / settings.CellCount / settings.PatchVertices, size.z / settings.CellCount / settings.PatchVertices, position.x, position.z));
                    pass.SetFloat("_PatchUvScale", 1f / settings.CellCount);

                    pass.SetFloat("_HeightUvScale", 1f / settings.CellCount * (1.0f - 1f / terrainSystem.terrainData.heightmapResolution));
                    pass.SetFloat("_HeightUvOffset", 0.5f / terrainSystem.terrainData.heightmapResolution);

                    pass.SetFloat("_MaxLod", Mathf.Log(settings.CellCount, 2));

                    pass.SetInt("_CullingPlanesCount", cullingPlanes.Count);
                    var cullingPlanesArray = ArrayPool<Vector4>.Get(cullingPlanes.Count);
                    for (var i = 0; i < cullingPlanes.Count; i++)
                        cullingPlanesArray[i] = cullingPlanes.GetCullingPlaneVector4(i);

                    pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                    ArrayPool<Vector4>.Release(cullingPlanesArray);
                });
            }

            var cullingResults = renderGraph.GetResource<CullingResultsData>().CullingResults;
            var context = renderGraph.GetResource<RenderContextData>().Context;

            using (var pass = renderGraph.AddRenderPass<ObjectRenderPass>("Render Terrain Replacement"))
            {
                pass.Initialize("Terrain", context, cullingResults, viewData.Camera, RenderQueueRange.opaque, SortingCriteria.CommonOpaque);
                pass.WriteDepth(renderGraph.GetResource<CameraDepthData>(), RenderTargetFlags.None);
                pass.AddRenderPassData<ICommonPassData>();
            }
        }
    }
}