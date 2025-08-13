using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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
}