using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public interface ITerrainAlphamapModifier
    {
        bool NeedsUpdate { get; }
        // TODO: Encapsulate arguments in some kind of terrain layer data struct
        void PreGenerate(Dictionary<TerrainLayer, int> terrainLayers, Dictionary<TerrainLayer, int> proceduralLayers);
        void Generate(CommandBuffer command, Dictionary<TerrainLayer, int> terrainLayers, Dictionary<TerrainLayer, int> proceduralLayers, RenderTexture idMap);
    }

    public interface ITerrainRenderer
    {
        public RenderTexture Heightmap { get; set; }
        public RenderTexture NormalMap { get; set; }
    }
}