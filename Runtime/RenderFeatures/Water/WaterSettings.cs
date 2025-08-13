using System;
using UnityEngine;

[Serializable]
public class WaterSettings
{
    [field: SerializeField, Tooltip("Whether water is enabled or not by default. (Can be overridden in scene")] public bool IsEnabled { get; private set; } = true;
    [field: SerializeField, Tooltip("The resolution of the simulation, higher numbers give more detail but are more expensive")] public int Resolution { get; private set; } = 128;
    [field: SerializeField] public Material Material { get; private set; }
    [field: SerializeField] public WaterProfile Profile { get; private set; }
    [field: SerializeField] public float ShadowRadius { get; private set; } = 8192;
    [field: SerializeField] public float ShadowBias { get; private set; } = 0;
    [field: SerializeField] public float ShadowSlopeBias { get; private set; } = 0;
    [field: SerializeField] public int ShadowResolution { get; private set; } = 512;
    [field: SerializeField] public bool RaytracedRefractions { get; private set; } = false;

    [field: Header("Rendering")]
    [field: SerializeField] public int CellCount { get; private set; } = 32;
    [field: SerializeField, Tooltip("Size of the Mesh in World Space")] public int Size { get; private set; } = 256;
    [field: SerializeField] public int PatchVertices { get; private set; } = 32;
    [field: SerializeField, Range(1, 128)] public float EdgeLength { get; private set; } = 64;
    [field: SerializeField] public int CasuticsResolution { get; private set; } = 256;
    [field: SerializeField, Range(0, 3)] public int CasuticsCascade { get; private set; } = 0;
    [field: SerializeField, Min(0)] public float CausticsDepth { get; private set; } = 10;
}