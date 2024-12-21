using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Arycama.CustomRenderPipeline
{
    [Serializable]
    public class TerrainSettings
    {
        [field: SerializeField] public Material Material { get; private set; } = null;
        [field: SerializeField] public int CellCount { get; private set; } = 32;
        [field: SerializeField] public int PatchVertices { get; private set; } = 32;
        [field: SerializeField] public float EdgeLength { get; private set; } = 64;
        [field: SerializeField] public GraphicsFormat DiffuseFormat { get; private set; } = GraphicsFormat.RGBA_DXT5_SRGB;
        [field: SerializeField] public int DiffuseResolution { get; private set; } = 512;
        [field: SerializeField] public GraphicsFormat NormalFormat { get; private set; } = GraphicsFormat.RGBA_DXT5_UNorm;
        [field: SerializeField] public int NormalResolution { get; private set; } = 512;
        [field: SerializeField] public GraphicsFormat MaskFormat { get; private set; } = GraphicsFormat.RGBA_DXT5_UNorm;
        [field: SerializeField] public int MaskResolution { get; private set; } = 512;
    }
}