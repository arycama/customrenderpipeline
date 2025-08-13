using UnityEngine;
using UnityEngine.Rendering;

public interface ITerrainHeightmapModifier
{
	void Generate(CommandBuffer command, ResourceHandle<RenderTexture> targetHeightmap, RenderTexture originalHeightmap);
}