using UnityEngine;
using UnityEngine.Assertions;

namespace Arycama.CustomRenderPipeline
{
    public class TerrainShadowRenderer : RenderFeature
    {
        private readonly TerrainSystem.Settings settings;
        private readonly TerrainSystem terrainSystem;

        public TerrainShadowRenderer(RenderGraph renderGraph, TerrainSystem.Settings settings, TerrainSystem terrainSystem) : base(renderGraph)
        {
            this.settings = settings;
            this.terrainSystem = terrainSystem;
        }

        public override void Render()
        {
            var terrain = terrainSystem.terrain;
            var terrainData = terrainSystem.terrainData;

            if (terrainSystem.terrain == null || settings.Material == null)
                return;

            var viewData = renderGraph.GetResource<ViewData>();
            var shadowRequestData = renderGraph.GetResource<ShadowRequestData>();
            var shadowRequest = shadowRequestData.ShadowRequest;

            var passData = terrainSystem.Cull(viewData.ViewPosition, shadowRequest.CullingPlanes);

            var passIndex = settings.Material.FindPass("ShadowCaster");
            Assert.IsFalse(passIndex == -1, "Terrain Material has no ShadowCaster Pass");

            var size = terrainData.size;
            var position = terrain.GetPosition() - viewData.ViewPosition;

            using (var pass = renderGraph.AddRenderPass<DrawProceduralIndirectRenderPass>("Terrain Render"))
            {
                pass.Initialize(settings.Material, terrainSystem.indexBuffer, passData.IndirectArgsBuffer, MeshTopology.Quads, passIndex, null, shadowRequestData.Bias, shadowRequestData.SlopeBias, false);

                pass.WriteTexture(shadowRequestData.Shadow);
                pass.DepthSlice = shadowRequestData.CascadeIndex;

                pass.ReadBuffer("_PatchData", passData.PatchDataBuffer);
                pass.AddRenderPassData<TerrainRenderData>();
                pass.AddRenderPassData<AtmospherePropertiesAndTables>();
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

                    pass.SetFloat("_HeightUvScale", 1f / settings.CellCount * (1.0f - 1f / terrainData.heightmapResolution));
                    pass.SetFloat("_HeightUvOffset", 0.5f / terrainData.heightmapResolution);

                    pass.SetFloat("_MaxLod", Mathf.Log(settings.CellCount, 2));
                    pass.SetInt("_CullingPlanesCount", shadowRequest.CullingPlanes.Count);

                    var cullingPlanesArray = ArrayPool<Vector4>.Get(shadowRequest.CullingPlanes.Count);
                    for (var i = 0; i < shadowRequest.CullingPlanes.Count; i++)
                        cullingPlanesArray[i] = shadowRequest.CullingPlanes.GetCullingPlaneVector4(i);

                    pass.SetVectorArray("_CullingPlanes", cullingPlanesArray);
                    ArrayPool<Vector4>.Release(cullingPlanesArray);

                    pass.SetMatrix("_WorldToClip", shadowRequest.ProjectionMatrix * shadowRequest.ViewMatrix);
                });
            }
        }
    }
}