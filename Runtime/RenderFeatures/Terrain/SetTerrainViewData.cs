using System;
using UnityEngine;
using UnityEngine.Rendering;
using static Math;

public class SetTerrainViewData : ViewRenderFeature
{
    private readonly TerrainSystem terrainSystem;
    private readonly TerrainSettings settings;

    public SetTerrainViewData(RenderGraph renderGraph, TerrainSystem terrainSystem, TerrainSettings settings) : base(renderGraph)
    {
        this.terrainSystem = terrainSystem;
        this.settings = settings;
    }

    public override void Render(in ReadOnlySpan<ViewParameter> viewParameters, in ViewPassData viewPassData, in DisplayData displayOutputData, ScriptableRenderContext context)
    {
        var terrain = terrainSystem.Terrain;
        if (terrain == null)
            return;

        var position = terrainSystem.Terrain.GetPosition() - viewPassData.position;
        var size = (Float3)terrain.terrainData.size;

        renderGraph.SetResource<TerrainViewData>(new(renderGraph.SetConstantBuffer
        ((
            GraphicsUtilities.HalfTexelRemap(position.xz, size.xz, Vector2.one * terrain.terrainData.heightmapResolution),
            new Float4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z),
            position.y
        ))));

        renderGraph.SetResource(new TerrainQuadtreeData(renderGraph.SetConstantBuffer
        ((
            new Float4(size.xz, position.xz),
            GraphicsUtilities.HalfTexelRemap(terrain.terrainData.heightmapResolution),
            Rcp(settings.CellCount * settings.PatchVertices),
            Rcp(settings.PatchVertices),
            Rcp(settings.CellCount),
            settings.PatchVertices + 1,
            settings.PatchVertices,
            0
        ))));
    }
}
