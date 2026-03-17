
using System.Collections.Generic;
using UnityEngine;
using static Math;

public class TerrainViewData : ViewRenderFeature
{
    private readonly TerrainSystem terrainSystem;
    private readonly TerrainSettings settings;

    public TerrainViewData(RenderGraph renderGraph, TerrainSystem terrainSystem, TerrainSettings settings) : base(renderGraph)
    {
        this.terrainSystem = terrainSystem;
        this.settings = settings;
    }

    public override void Render(ViewRenderData viewRenderData)
    {
        var terrain = terrainSystem.Terrain;
        if (terrain == null)
            return;

        var position = terrainSystem.Terrain.GetPosition() - viewRenderData.transform.position;
        var size = (Float3)terrain.terrainData.size;

        renderGraph.SetResource<TerrainRenderData>
        (
            new
            (
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
                (
                    new Pass0Data
                    (
                        size,
                        terrainSystem.TerrainData.alphamapResolution,
                        terrainSystem.Terrain.GetPosition(),
                        size.y,
                        GraphicsUtilities.HalfTexelRemap(position.xz, size.xz, Vector2.one * terrainSystem.TerrainData.heightmapResolution),
                        new Float4(1f / size.x, 1f / size.z, -position.x / size.x, -position.z / size.z),
                        GraphicsUtilities.HalfTexelRemap(terrain.terrainData.heightmapResolution),
                        position.y,
                        terrain.terrainData.heightmapResolution
                    )
                )
            )
        );

        renderGraph.SetResource(new TerrainQuadtreeData(renderGraph.SetConstantBuffer
        (
            new Pass1Data
            (
                new Float4(size.xz, position.xz),
                GraphicsUtilities.HalfTexelRemap(terrain.terrainData.heightmapResolution),
                Rcp(settings.CellCount * settings.PatchVertices),
                Rcp(settings.PatchVertices),
                Rcp(settings.CellCount),
                settings.PatchVertices + 1,
                settings.PatchVertices,
                0
            )
        )));
    }

    private struct Pass0Data
    {
        public Float3 size;
        public float Item2;
        public Vector3 Item3;
        public float Item4;
        public Float4 Item5;
        public Float4 Item6;
        public Float2 Item7;
        public float Item8;
        public float Item9;

        public Pass0Data(Float3 size, float item2, Vector3 item3, float item4, Float4 item5, Float4 item6, Float2 item7, float item8, float item9)
        {
            this.size = size;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
            Item8 = item8;
            Item9 = item9;
        }
    }

    private readonly struct Pass1Data
    {
        public readonly Float4 Item1;
        public readonly Float2 Item2;
        public readonly float Item3;
        public readonly float Item4;
        public readonly float Item5;
        public readonly int Item6;
        public readonly int PatchVertices;
        public readonly int Item8;

        public Pass1Data(Float4 item1, Float2 item2, float item3, float item4, float item5, int item6, int patchVertices, int item8)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            PatchVertices = patchVertices;
            Item8 = item8;
        }
    }
}