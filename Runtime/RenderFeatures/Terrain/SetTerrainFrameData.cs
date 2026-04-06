using UnityEngine.Rendering;

public class SetTerrainFrameData : FrameRenderFeature
{
    private readonly TerrainSystem terrainSystem;

    public SetTerrainFrameData(RenderGraph renderGraph, TerrainSystem terrainSystem) : base(renderGraph)
    {
        this.terrainSystem = terrainSystem;
    }

    public override void Render(ScriptableRenderContext context)
    {
        var terrain = terrainSystem.Terrain;
        if (terrain == null)
            return;

        var size = (Float3)terrain.terrainData.size;

        renderGraph.SetResource<TerrainFrameData>
        (new(
            terrainSystem.diffuseArray,
            terrainSystem.normalMapArray,
            terrainSystem.maskMapArray,
            terrainSystem.heightmap,
            terrainSystem.normalmap,
            terrainSystem.idMap,
            terrainSystem.TerrainData.holesTexture,
            terrainSystem.terrainLayerData,
            terrainSystem.aoMap,
            renderGraph.SetConstantBuffer
            ((
                size,
                terrainSystem.TerrainData.alphamapResolution,
                GraphicsUtilities.HalfTexelRemap(terrain.terrainData.heightmapResolution),
                size.y,
                terrain.terrainData.heightmapResolution
            ))
        ));
    }
}