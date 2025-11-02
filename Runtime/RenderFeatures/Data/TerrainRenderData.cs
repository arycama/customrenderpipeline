using UnityEngine;
using UnityEngine.Rendering;

public readonly struct TerrainRenderData : IRenderPassData
{
	private static readonly int _TerrainHolesTextureId = Shader.PropertyToID("_TerrainHolesTexture");
	private static readonly int AlbedoSmoothnessId = Shader.PropertyToID("AlbedoSmoothness");
	private static readonly int NormalId = Shader.PropertyToID("Normal");
	private static readonly int MaskId = Shader.PropertyToID("Mask");

	private readonly Texture2DArray albedoSmoothness, normal, mask;
	private readonly ResourceHandle<RenderTexture> terrainHeightmapTexture, terrainNormalMap, idMap, aoMap;
	private readonly Texture terrainHolesTexture;
	private readonly ResourceHandle<GraphicsBuffer> terrainLayerData;
	private readonly ResourceHandle<GraphicsBuffer> terrainData;

	public TerrainRenderData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray mask, ResourceHandle<RenderTexture> terrainHeightmapTexture, ResourceHandle<RenderTexture> terrainNormalMap, ResourceHandle<RenderTexture> idMap, Texture terrainHolesTexture, ResourceHandle<GraphicsBuffer> terrainLayerData, ResourceHandle<RenderTexture> aoMap, ResourceHandle<GraphicsBuffer> terrainData)
	{
		this.albedoSmoothness = albedoSmoothness;
		this.normal = normal;
		this.mask = mask;
		this.terrainHeightmapTexture = terrainHeightmapTexture;
		this.terrainNormalMap = terrainNormalMap;
		this.idMap = idMap;
		this.terrainHolesTexture = terrainHolesTexture;
		this.terrainLayerData = terrainLayerData;
		this.aoMap = aoMap;
		this.terrainData = terrainData;
	}

	public readonly void SetInputs(RenderPass pass)
	{
		pass.ReadTexture("TerrainHeightmap", terrainHeightmapTexture);
		pass.ReadTexture("TerrainNormalMap", terrainNormalMap);
		pass.ReadTexture("IdMap", idMap);
		pass.ReadTexture("BentNormalVisibility", aoMap);
		pass.ReadBuffer("TerrainLayerData", terrainLayerData);
		pass.ReadBuffer("TerrainData", terrainData);
	}

	public readonly void SetProperties(RenderPass pass, CommandBuffer command)
	{
		pass.SetTexture(_TerrainHolesTextureId, terrainHolesTexture);

		pass.SetTexture(AlbedoSmoothnessId, albedoSmoothness);
		pass.SetTexture(NormalId, normal);
		pass.SetTexture(MaskId, mask);
	}
}
