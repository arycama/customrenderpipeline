using Arycama.CustomRenderPipeline;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public class TerrainSystem
{
    [Serializable]
    public class Settings
    {
        [field: SerializeField] public int CellCount { get; private set; } = 32;
        [field: SerializeField] public int PatchVertices { get; private set; } = 32;
        [field: SerializeField] public float EdgeLength { get; private set; } = 64;
    }

    private bool heightmapDirty, isInitialized;

    private ComputeBuffer patchDataBuffer, indirectArgsBuffer, lodIndirectArgsBuffer;

    private GraphicsBuffer indexBuffer;
    private RenderTexture minMaxHeight;
    private Terrain terrain;

    private int VerticesPerTileEdge => settings.PatchVertices + 1;
    private int QuadListIndexCount => settings.PatchVertices * settings.PatchVertices * 4;
    private int Resolution => terrain.terrainData.heightmapResolution;

    private Settings settings;
    private RenderGraph renderGraph;

    public RenderTexture Heightmap { get; private set; }
    public RenderTexture NormalMap { get; private set; }

    public TerrainSystem(RenderGraph renderGraph, Settings settings)
    {
        this.renderGraph = renderGraph;
        this.settings = settings;

        TerrainCallbacks.heightmapChanged += OnHeightmapChanged;
    }

    private void InitializeTerrain()
    {
        terrain = Terrain.activeTerrain;

        var resolution = terrain.terrainData.heightmapResolution;
        minMaxHeight = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RG32)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            name = "Terrain Min Max Height Map",
            useMipMap = true,
        }.Created();

        Heightmap = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.R16)
        {
            enableRandomWrite = true,
            name = "Terrain Height Map",
        }.Created();

        NormalMap = new RenderTexture(resolution, resolution, 0, GraphicsFormat.R8G8_SNorm)
        {
            autoGenerateMips = false,
            enableRandomWrite = true,
            name = "Terrain Normal Map",
            useMipMap = true,
        }.Created();

        heightmapDirty = true;

        //if (terrainGraph != null)
        //a    terrainGraph.AddListener(OnGraphModified, 0);

        indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, QuadListIndexCount, sizeof(ushort));

        int index = 0;
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

        indexBuffer.SetData(pIndices);
        //CullTerrainNode.Render += Cull;
        //DrawTerrainNode.Render += Render;

        lodIndirectArgsBuffer = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "Terrain Indirect Args" };
        indirectArgsBuffer = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments) { name = "Terrain Draw Args" };
        patchDataBuffer = new ComputeBuffer(settings.CellCount * settings.CellCount, sizeof(uint), ComputeBufferType.Structured) { name = "Terrain Patch Data" };
    }

    private void CleanupTerrain()
    {

    }

    public void Update()
    {
        var activeTerrain = Terrain.activeTerrain;
        if(terrain != activeTerrain)
        {
            if (terrain != null)
                CleanupTerrain();

            InitializeTerrain();
        }
    }

    public void Cull()
    {

    }

    public void Render()
    {

    }

    private void OnHeightmapChanged(Terrain terrain, RectInt heightRegion, bool synched)
    {
        if (terrain != this.terrain)
            return;

        heightmapDirty = true;
    }
}
