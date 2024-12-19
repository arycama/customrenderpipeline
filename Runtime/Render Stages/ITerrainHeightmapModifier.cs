using UnityEngine;
using UnityEngine.Rendering;

namespace Arycama.CustomRenderPipeline
{
    public interface ITerrainHeightmapModifier
    {
        void Generate(CommandBuffer command, RTHandle targetHeightmap, RenderTexture originalHeightmap);
    }
}