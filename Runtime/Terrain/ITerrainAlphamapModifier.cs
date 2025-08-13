using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public interface ITerrainAlphamapModifier
{
	bool NeedsUpdate { get; }
	// TODO: Encapsulate arguments in some kind of terrain layer data struct
	void PreGenerate(Dictionary<TerrainLayer, int> terrainLayers, Dictionary<TerrainLayer, int> proceduralLayers);
	void Generate(CommandBuffer command, Dictionary<TerrainLayer, int> terrainLayers, Dictionary<TerrainLayer, int> proceduralLayers, RenderTexture idMap);
}
