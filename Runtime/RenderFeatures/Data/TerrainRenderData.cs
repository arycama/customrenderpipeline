using UnityEngine;
using UnityEngine.Rendering;

public struct TerrainRenderData : IRenderPassData
{
	private readonly Texture2DArray albedoSmoothness, normal, mask;
	private readonly ResourceHandle<RenderTexture> terrainHeightmapTexture, terrainNormalMap, idMap, aoMap;
	private readonly Texture terrainHolesTexture;
	private Vector4 terrainRemapHalfTexel, terrainScaleOffset;
	private Vector3 terrainSize;
	private readonly float terrainHeightScale, terrainHeightOffset, idMapResolution;
	private readonly ResourceHandle<GraphicsBuffer> terrainLayerData;

	public TerrainRenderData(Texture2DArray albedoSmoothness, Texture2DArray normal, Texture2DArray mask, ResourceHandle<RenderTexture> terrainHeightmapTexture, ResourceHandle<RenderTexture> terrainNormalMap, ResourceHandle<RenderTexture> idMap, Texture terrainHolesTexture, Vector4 terrainRemapHalfTexel, Vector4 terrainScaleOffset, Vector3 terrainSize, float terrainHeightScale, float terrainHeightOffset, float idMapResolution, ResourceHandle<GraphicsBuffer> terrainLayerData, ResourceHandle<RenderTexture> aoMap)
	{
		this.albedoSmoothness = albedoSmoothness;
		this.normal = normal;
		this.mask = mask;
		this.terrainHeightmapTexture = terrainHeightmapTexture;
		this.terrainNormalMap = terrainNormalMap;
		this.idMap = idMap;
		this.terrainHolesTexture = terrainHolesTexture;
		this.terrainRemapHalfTexel = terrainRemapHalfTexel;
		this.terrainScaleOffset = terrainScaleOffset;
		this.terrainSize = terrainSize;
		this.terrainHeightScale = terrainHeightScale;
		this.terrainHeightOffset = terrainHeightOffset;
		this.idMapResolution = idMapResolution;
		this.terrainLayerData = terrainLayerData;
		this.aoMap = aoMap;
	}

	public readonly void SetInputs(RenderPassBase pass)
	{
		pass.ReadTexture("TerrainHeightmap", terrainHeightmapTexture);
		pass.ReadTexture("TerrainNormalMap", terrainNormalMap);
		pass.ReadTexture("IdMap", idMap);
		pass.ReadTexture("BentNormalVisibility", aoMap);
		pass.ReadBuffer("TerrainLayerData", terrainLayerData);
	}

	public readonly void SetProperties(RenderPassBase pass, CommandBuffer command)
	{
		pass.SetTexture("_TerrainHolesTexture", terrainHolesTexture);

		pass.SetTexture("AlbedoSmoothness", albedoSmoothness);
		pass.SetTexture("Normal", normal);
		pass.SetTexture("Mask", mask);

		pass.SetVector("TerrainSize", terrainSize);
		pass.SetVector("_TerrainRemapHalfTexel", terrainRemapHalfTexel);
		pass.SetVector("_TerrainScaleOffset", terrainScaleOffset);

		pass.SetFloat("IdMapResolution", idMapResolution);
		pass.SetFloat("_TerrainHeightScale", terrainHeightScale);
		pass.SetFloat("_TerrainHeightOffset", terrainHeightOffset);
	}
}
