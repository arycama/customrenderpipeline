using UnityEngine;

public interface ITerrainRenderer
{
	public RenderTexture Heightmap { get; set; }
	public RenderTexture NormalMap { get; set; }
}