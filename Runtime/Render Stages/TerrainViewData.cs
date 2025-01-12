using UnityEngine;

namespace Arycama.CustomRenderPipeline
{
    public class TerrainViewData : RenderFeature
    {
        private readonly TerrainSystem terrainSystem;

        public TerrainViewData(RenderGraph renderGraph, TerrainSystem terrainSystem) : base(renderGraph)
        {
            this.terrainSystem = terrainSystem;
        }

        public override void Render()
        {
            var viewData = renderGraph.GetResource<ViewData>();
            var position = terrainSystem.terrain.GetPosition() - viewData.ViewPosition;
            var size = terrainSystem.terrainData.size;
            var terrainScaleOffset = new Vector4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z);
            var terrainRemapHalfTexel = GraphicsUtilities.HalfTexelRemap(position.XZ(), size.XZ(), Vector2.one * terrainSystem.terrainData.heightmapResolution);
            var terrainHeightOffset = position.y;
            renderGraph.SetResource(new TerrainRenderData(terrainSystem.diffuseArray, terrainSystem.normalMapArray, terrainSystem.maskMapArray, terrainSystem.heightmap, terrainSystem.normalmap, terrainSystem.idMap, terrainSystem.terrainData.holesTexture, terrainRemapHalfTexel, terrainScaleOffset, size, size.y, terrainHeightOffset, terrainSystem.terrainData.alphamapResolution, terrainSystem.terrainLayerData));

            // This sets raytracing data on the terrain's material property block
            using (var pass = renderGraph.AddRenderPass<SetPropertyBlockPass>("Terrain Data Property Block Update"))
            {
                var propertyBlock = pass.propertyBlock;
                terrainSystem.terrain.GetSplatMaterialPropertyBlock(propertyBlock);
                pass.AddRenderPassData<TerrainRenderData>();

                pass.SetRenderFunction((command, pass) =>
                {
                    terrainSystem.terrain.SetSplatMaterialPropertyBlock(propertyBlock);
                });
            }
        }
    }
}