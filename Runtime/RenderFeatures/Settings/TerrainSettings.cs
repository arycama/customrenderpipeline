using System;
using UnityEngine;

[Serializable]
public class TerrainSettings
{
	[field: SerializeField] public Material Material { get; private set; } = null;
	[field: SerializeField] public int CellCount { get; private set; } = 32;
	[field: SerializeField] public int PatchVertices { get; private set; } = 32;
	[field: SerializeField] public float EdgeLength { get; private set; } = 64;
	[field: SerializeField, Min(0)] public float AmbientOcclusionRadius { get; private set; } = 1;
	[field: SerializeField, Range(1, 128)] public int AmbientOcclusionDirections { get; private set; } = 32;
	[field: SerializeField, Range(1, 1024)] public int AmbientOcclusionSamples { get; private set; } = 32;
	[field: SerializeField] public int TileSize { get; private set; } = 256;
    [field: SerializeField, Pow2(512)] public int TileResolution { get; private set; } = 256;
	[field: SerializeField] public int VirtualResolution { get; private set; } = 524288;
	[field: SerializeField, Pow2(2048)] public int VirtualTileCount { get; private set; } = 512;
	[field: SerializeField, Range(1, 16)] public int AnisoLevel { get; private set; } = 4;
	[field: SerializeField, Pow2(32)] public int UpdateTileCount { get; private set; } = 8;
}